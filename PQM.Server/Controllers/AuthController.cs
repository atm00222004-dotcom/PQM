using Microsoft.AspNetCore.Mvc;
using PQM.Core.Entities;
using PQM.Core.Interfaces.Repositories;
using PQM.Core.DTOs;

namespace PQM.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthRepository _authRepository;

        public AuthController(IAuthRepository authRepository)
        {
            _authRepository = authRepository?? throw new ArgumentNullException(nameof(authRepository));
        }

        [HttpPost("signup")]
        public async Task<IActionResult> SignUp([FromBody] SignUpDto dto,CancellationToken cancellationToken)
        {
            if (await _authRepository.EmailExistsAsync(dto.Email, cancellationToken))
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
                CreatedAt = DateTime.UtcNow
            };

            int userId = await _authRepository.AddAsync(user, cancellationToken);

            return Ok(new
            {
                token = $"token-{userId}-{user.Email}"
            });
        }

        [HttpPost("signin")]
        public async Task<IActionResult> SignIn([FromBody] SignInDto dto,CancellationToken cancellationToken)
        {
            var user = await _authRepository.GetByEmailAndPasswordAsync(
                dto.Email,
                dto.Password,
                cancellationToken);

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
        public async Task<IActionResult> GetMe([FromQuery] string token,CancellationToken cancellationToken)
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

            var user = await _authRepository.GetByIdAsync(
                userId,
                cancellationToken);

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