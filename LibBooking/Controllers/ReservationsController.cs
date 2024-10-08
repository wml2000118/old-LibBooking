﻿using LibBooking.Data;
using LibBooking.Models;
using LibBooking.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace LibBooking.Controllers
{
    [Route("api/[controller]")]

    public class ReservationsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;

        public ReservationsController(ApplicationDbContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        [HttpGet("index")]
        public IActionResult Index(DateTime? date)
        {
            if (date == null)
            {
                date = DateTime.Today;
            }

            // 获取所有房间信息
            var rooms = _context.Rooms.ToList();

            // 获取指定日期的所有预定
            var reservations = _context.Reservations
                .Where(r => r.ReservationDate == date)
                .ToList();

            // 将房间信息和预定信息传递到视图
            var model = new RoomReservationViewModel
            {
                Date = date.Value,
                Rooms = rooms,
                Reservations = reservations
            };

            return View(model);
        }


        // 获取预订信息的 API 端点
        [HttpGet("get-reservations")]
        public async Task<ActionResult<IEnumerable<Reservation>>> GetReservations([FromQuery] int? roomID, [FromQuery] DateTime? start, [FromQuery] DateTime? end)
        {
            var query = _context.Reservations.AsQueryable();

            if (roomID.HasValue)
            {
                query = query.Where(r => r.RoomID == roomID);
            }

            if (start.HasValue && end.HasValue)
            {
                query = query.Where(r => r.ReservationDate >= start && r.ReservationDate <= end);
            }

            return await query.Include(r => r.Room).ToListAsync();
        }

        // 提交时间方法
        [HttpPost("submit-time")]
        public IActionResult SubmitTime(int roomID, int time, DateTime reservationDate)
        {
            // 验证传入的数据
            if (roomID <= 0 || time < 0 || time > 23)
            {
                return BadRequest(new { message = "Invalid input data." });
            }

            // 查找所选的房间
            var selectedRoom = _context.Rooms.FirstOrDefault(r => r.ID == roomID);
            if (selectedRoom == null)
            {
                return BadRequest(new { message = "Invalid Room ID." });
            }

            // 构建 EmailValidationViewModel 模型
            var model = new EmailValidationViewModel
            {
                Room = selectedRoom.RoomName,
                ID = roomID,
                Time = time,
                Date = reservationDate
            };

            // 返回视图，并将模型传递到视图中
            return View("EmailValidation", model);
        }




        // 验证邮箱
        [HttpPost("email-validation")]
        public async Task<IActionResult> EmailValidation(EmailValidationViewModel model)
        {
            Console.WriteLine($"EmailValidation called with Room: {model.Room}, ID: {model.ID}, Time: {model.Time}");

            var validDomains = new[] { "@student.weltec.ac.nz", "@weltec.ac.nz" };
            var emailDomain = model.Email.Substring(model.Email.IndexOf("@"));

            if (!validDomains.Contains(emailDomain))
            {
                TempData["error"] = "Please use a valid university email address (@student.weltec.ac.nz or @weltec.ac.nz).";
                return View(model);
            }

            var selectedRoom = _context.Rooms.FirstOrDefault(r => r.ID == model.ID);
            if (selectedRoom == null)
            {
                TempData["error"] = "Invalid Room ID.";
                return View(model);
            }

            try
            {
                int timeAsInt = int.Parse(model.Time.ToString());

                var startTime = new TimeSpan(timeAsInt, 0, 0); // 使用转换后的整数来创建 TimeSpan
                // 解析时间部分为整型
                var endTime = startTime.Add(TimeSpan.FromHours(1));

                var reservation = new Reservation
                {
                    RoomID = model.ID,
                    ReservationDate = model.Date,
                    StartTime = startTime,
                    EndTime = endTime,
                    Email = model.Email
                };

                _context.Reservations.Add(reservation);
                await _context.SaveChangesAsync();
                TempData["Message"] = $"Booking confirmed! {model.Room} at {model.Time}:00 on {model.Date:yyyy-MM-dd}.";
                //TempData["Message"] = "success";
                TempData["MessageType"] = "success"; // 或 "error" 根据情况设置

                Console.WriteLine("Reservation saved to database.");

                var message = $@"
<p>Dear {model.Email},</p>

<p>We are pleased to confirm that you have successfully booked the room <strong>{model.Room}</strong> on <strong>{model.Date:dddd, MMMM dd, yyyy}</strong> at <strong>{model.Time}:00</strong>.</p>

<p><strong>Room:</strong> {model.Room}<br>
<strong>Date:</strong> {model.Date:dddd, MMMM dd, yyyy}<br>
<strong>Time:</strong> {model.Time}:00</p>

<p>If you need to cancel or modify your booking, please do so at least 24 hours before the reserved time.</p>

<p>Thank you for using our booking service. We look forward to serving you.</p>

<p>Best regards,<br>
The Library Team</p>";

                // 发送HTML格式的邮件
                await _emailService.SendEmailAsync(model.Email, "Room Booking Confirmation", message);



                Console.WriteLine("Confirmation email sent.");

                //ViewBag.Message = "提交成功，预订确认邮件已发送到您的邮箱。";
                ///*return View(model);*/ // 返回当前视图，并显示成功消息
                //return View("Index", new RoomReservationViewModel
                //{
                //    Date = DateTime.Today,
                //    Rooms = _context.Rooms.ToList(),
                //    Reservations = _context.Reservations.Where(r => r.ReservationDate == DateTime.Today).ToList()
                //});
                // Redirect to avoid form resubmission issue
                return RedirectToAction("Index", new { date = model.Date });

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving reservation: {ex.Message}");
                TempData["error"] = $"An error occurred while processing your booking: {ex.Message}";

                return View(model); // 返回当前视图，并显示错误消息
            }
        }




        // 创建预订
        [HttpPost]
        public async Task<ActionResult<Reservation>> CreateReservation([FromBody] Reservation reservationDto)
        {
            var validEmailDomains = new[] { "@student.weltec.ac.nz", "@weltec.ac.nz" };
            var emailDomain = reservationDto.Email.Substring(reservationDto.Email.IndexOf("@"));

            if (!validEmailDomains.Contains(emailDomain))
            {
                return BadRequest("Invalid email domain. Please use a valid WelTec email address.");
            }

            var reservation = new Reservation
            {
                Email = reservationDto.Email,
                RoomID = reservationDto.RoomID,
                ReservationDate = reservationDto.ReservationDate,
                StartTime = reservationDto.StartTime,
                EndTime = reservationDto.EndTime
            };

            _context.Reservations.Add(reservation);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetReservations), new { id = reservation.ID }, reservation);
        }

        //// GET: Manage reservations - Admin only
        //[HttpGet("manage")]
        //[Authorize(Roles = "Admin")]
        //public async Task<IActionResult> Manage()
        //{
        //    var reservations = await _context.Reservations.ToListAsync();
        //    return View(reservations);
        //}

        [HttpGet("manage")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Manage()
        {
            var reservations = await _context.Reservations
                .Include(r => r.Room) // Include Room data
                .ToListAsync();
            return View(reservations);
        }

        // GET: Reservations/Edit/5
        //[HttpGet("edit/{id}")]
        //[Authorize(Roles = "Admin")]
        //public async Task<IActionResult> Edit(int? id)
        //{
        //    if (id == null)
        //    {
        //        return NotFound();
        //    }

        //    var reservation = await _context.Reservations.FindAsync(id);
        //    if (reservation == null)
        //    {
        //        return NotFound();
        //    }
        //    return View(reservation);
        //}

        [HttpGet("edit/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            // Fetch the reservation along with the room information
            var reservation = await _context.Reservations
                .Include(r => r.Room)
                .FirstOrDefaultAsync(r => r.ID == id);

            if (reservation == null)
            {
                return NotFound();
            }

            // Pass the Room Name to the view using ViewBag
            ViewBag.RoomName = reservation.Room.RoomName;

            return View(reservation); // Pass the Reservation model to the view
        }

        // POST: Reservations/Edit/5
        [HttpPost("edit/{id}")]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Reservation reservation)
        {
            ModelState.Remove("Room");

            if (id != reservation.ID)
            {
                //TempData["error"] = "Reservation not found.";
                TempData["Message"] = "Reservation not found.";
                TempData["MessageType"] = "error";
                return NotFound();
            }

            // Check for overlapping reservations
            var conflictingReservation = await _context.Reservations
                .Where(r => r.RoomID == reservation.RoomID
                            && r.ID != reservation.ID
                            && r.ReservationDate == reservation.ReservationDate
                            && ((reservation.StartTime >= r.StartTime && reservation.StartTime < r.EndTime)
                            || (reservation.EndTime > r.StartTime && reservation.EndTime <= r.EndTime)))
                .FirstOrDefaultAsync();

            if (conflictingReservation != null)
            {
                TempData["error"] = "The selected time conflicts with another reservation.";
                return View(reservation);
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(reservation);
                    await _context.SaveChangesAsync();
                    //TempData["success"] = "Reservation updated successfully.";
                    TempData["Message"] = $"Booking updated!";
                    //TempData["Message"] = "success";
                    TempData["MessageType"] = "success";
                    return RedirectToAction(nameof(Manage)); // Redirect to Manage view after saving
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Reservations.Any(e => e.ID == reservation.ID))
                    {
                        //TempData["error"] = "Reservation not found.";
                        TempData["Message"] = "Reservation not found.";
                        TempData["MessageType"] = "error";
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
            ViewBag.Errors = errors;
            //TempData["error"] = "Error updating reservation.";
            TempData["Message"] = "Error updating reservation.";
            TempData["MessageType"] = "error";
            return View(reservation);
        }


        // GET: Reservations/Delete/5
        [HttpGet("delete/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var reservation = await _context.Reservations
                .FirstOrDefaultAsync(m => m.ID == id);
            if (reservation == null)
            {
                return NotFound();
            }

            return View(reservation);
        }

        // POST: Reservations/Delete/5
        [HttpPost("delete/{id}"), ActionName("Delete")]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var reservation = await _context.Reservations.FindAsync(id);
            _context.Reservations.Remove(reservation);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpGet("manage-reservations")]
        [Authorize(Roles = "Admin")]
        public IActionResult ManageReservations()
        {
            return View();  // This loads the Manage Reservations page
        }

        #region API CALLS

        [HttpGet("get-all-reservations")]
        public IActionResult GetAllReservations()
        {
            var reservationList = _context.Reservations
                .Include(r => r.Room)  // Assuming Reservation has a Room relation
                .ToList();
            return Json(new { data = reservationList });
        }

        [HttpDelete("delete-reservation/{id}")]
        public IActionResult DeleteReservation(int id)
        {
            var reservation = _context.Reservations.FirstOrDefault(r => r.ID == id);
            if (reservation == null)
            {
                return Json(new { success = false, message = "Error while deleting" });
            }

            _context.Reservations.Remove(reservation);
            _context.SaveChanges();
            return Json(new { success = true, message = "Delete successful" });
        }

        #endregion

    }
}
