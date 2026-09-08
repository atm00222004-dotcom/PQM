using Microsoft.AspNetCore.Mvc;
using PQM.Core.Entities;
using PQM.Infrastructure;
using PQM.Server.Models;

namespace PQM.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly DataContext _db;

        public AuthController(DataContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        [HttpPost("signup")]
        public IActionResult SignUp([FromBody] SignUpDto dto)
        {
            if (_db.User.Any(u => u.Email == dto.Email))
            {
                return BadRequest(new
                {
                    error = "Email already in use."
                });
            }

            var user = new User
            {
                Username = dto.Email.Split('@')[0],
                Email = dto.Email,
                Password = dto.Password,
                CreatedDate = DateTime.UtcNow
            };

            _db.User.Add(user);
            _db.SaveChanges();

            return Ok(new
            {
                token = $"token-{user.Id}-{user.Email}"
            });
        }

        [HttpPost("signin")]
        public IActionResult SignIn([FromBody] SignInDto dto)
        {
            var user = _db.User.FirstOrDefault(
                u => u.Email == dto.Email &&
                     u.Password == dto.Password
            );

            if (user == null)
            {
                return BadRequest(new
                {
                    error = "Invalid email or password."
                });
            }

            return Ok(new
            {
                token = $"token-{user.Id}-{user.Email}"
            });
        }

        [HttpGet("me")]
        public IActionResult GetMe([FromQuery] string token)
        {
            if (string.IsNullOrEmpty(token) ||
                !token.StartsWith("token-"))
            {
                return Unauthorized();
            }

            var parts = token.Split('-');

            if (parts.Length < 3 ||
                !int.TryParse(parts[1], out int userId))
            {
                return Unauthorized();
            }

            var user = _db.User.FirstOrDefault(
                u => u.Id == userId
            );

            if (user == null)
            {
                return Unauthorized();
            }

            return Ok(new
            {
                id = $"USR-{user.Id}",
                email = user.Email,
                firstName = user.Username,
                lastName = ""
            });
        }
    }
}