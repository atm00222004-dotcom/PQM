namespace PQM.Server.Models
{
    public class SignUpDto
    {
        public required string Email { get; set; }
        public required string Password { get; set; }
    }

    public class SignInDto
    {
        public required string Email { get; set; }
        public required string Password { get; set; }
    }
}

