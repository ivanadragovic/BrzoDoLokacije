using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using backend.Models;
using backend.ModelsDto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "korisnik")]
    public class UserController : ControllerBase
    {
        private readonly DataContext _context;
        private readonly IConfiguration _configuration;
        private static readonly HttpClient _client = new HttpClient();

        public UserController(DataContext context,IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpGet("profileInfo/{username}")]
        public async Task<ActionResult<UserProfileDto>> getProfileInfo(string username)
        {
            var me = await _context.Users.FirstOrDefaultAsync(u => u.Username == User.Identity.Name);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null || me==null)
                return BadRequest(new
                {
                    error = true,
                    message = "User don't exist"
                });
            int followers = (await _context.Follows.Where(f => f.FolloweeId == user.Id).ToListAsync()).Count;
            int following = (await _context.Follows.Where(f => f.FollowerId == user.Id).ToListAsync()).Count;
            var posts = await _context.Posts.Where(p => p.UserId == user.Id).ToListAsync();
            int numOfLikes = 0;
            foreach (Post post in posts)
            {
                numOfLikes += (await _context.Likes.Where(l => l.PostId == post.Id).ToListAsync()).Count;
            }

            var followed = await _context.Follows.FirstOrDefaultAsync(f => f.FollowerId == me.Id && f.FolloweeId == user.Id);
            UserProfileDto upd = new UserProfileDto
            {
                Username = user.Username,
                Name = user.Name,
                Description = user.Description,
                Followers = followers,
                Following = following,
                NumberOfLikes = numOfLikes,
                NumberOfPosts = posts.Count,
                IsFollowed = followed==null ? false : true
                //Posts = await _context.Posts.Where(p => p.UserId == user.Id).OrderByDescending(p => p.Date).ToListAsync()
            };
            string json = JsonSerializer.Serialize(upd);
            return Ok(new
            {
                error = false,
                message = json
            });
        }
        
        [AllowAnonymous]
        [HttpGet("avatar/{username}")]
        public async Task<IActionResult> GetAvatar(string username)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            Byte[] b = System.IO.File.ReadAllBytes(user.Avatar);
            string[] types = user.Avatar.Split(".");
            string type =types[types.Length-1];
            return File(b, "image/"+type);
        }

        [HttpPut("update")]
        public async Task<ActionResult<string>> updateProfile(UpdateProfileDto request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == User.Identity.Name);
            if (user == null)
                return BadRequest(new
                {
                    error = true,
                    message = "Error"
                });
            if (user.Username != request.Username)
            {
                var userDB = await _context.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
                if (userDB != null)
                    return Ok(new
                    {
                        error = true,
                        message = "Someone else's using that username!"
                    });
            }

            user.Username = request.Username;
            user.Name = request.Name;
            user.Description = request.Description;
            await _context.SaveChangesAsync();
            string token = CreateToken(user);
            return Ok(new {
                error = false,
                message = token
            });
        }
        
        [HttpPut("updateAvatar")]
        public async Task<ActionResult<string>> updateAvatar(IFormFile picture)
        {
            if (picture == null)
                return Ok(new
                {
                    error = false,
                    message = "not changed"
                });
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == User.Identity.Name);
            if (user == null)
                return BadRequest(new
                {
                    error = true,
                    message = "Error"
                });
            
            using var _x = picture.OpenReadStream();

            var fileStreamContent = new StreamContent(_x);
            fileStreamContent.Headers.ContentType = new MediaTypeHeaderValue("image/*");

            var multipartFormContent = new MultipartFormDataContent();
            multipartFormContent.Add(fileStreamContent, name: "picture", fileName: picture.FileName);

            var url = _configuration.GetSection("Microservice").Value + "/avatar";

            string responseString = null;
            try 
            {
                var response = await _client.PostAsync(url, multipartFormContent);
                response.EnsureSuccessStatusCode();
                responseString = await response.Content.ReadAsStringAsync();
            }
            catch(HttpRequestException e)
            {
                Console.WriteLine("\nException Caught!");	
                Console.WriteLine("Message :{0} ", e.Message);

                return BadRequest(new
                {
                    error=true,
                    message="Error with microservice"
                });
            }

            if (responseString.Contains("false"))
                return Ok(new
                {
                    error = true,
                    message = "Avatar must contain only one face and be in a frontal view"
                });
            
            string path = CreatePathToDataRoot(user.Id, picture.FileName);
            var stream = new FileStream(path, FileMode.Create);
            await picture.CopyToAsync(stream);
            stream.Close();
            user.Avatar = path;
            await _context.SaveChangesAsync();
            return Ok(new
            {
                error = false,
                message = path
            });

        }
        [HttpPost("follow/{username}")]
        public async Task<ActionResult<string>> followUnfollow(string username)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == User.Identity.Name);
            var userToFollow = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null || userToFollow == null)
                return BadRequest(new
                {
                    error = true,
                    message = "Error"
                });
            var follow = await _context.Follows.FirstOrDefaultAsync(f => f.FollowerId == user.Id && f.FolloweeId == userToFollow.Id);
            if (follow == null)
            {
                Follow f = new Follow
                {
                    Follower = user,
                    Followee = userToFollow,
                    FollowerId = user.Id,
                    FolloweeId = userToFollow.Id
                };
                _context.Follows.Add(f);
                await _context.SaveChangesAsync();
                return Ok(new
                {
                    error = false,
                    message = "followed"
                });
            }
            else
            {
                _context.Follows.Remove(follow);
                await _context.SaveChangesAsync();
                return Ok(new
                {
                    error = false,
                    message = "unfollowed"
                });
            }

        }

        [HttpGet("followers/{username}")]
        public async Task<ActionResult<string>> getFollowers(string username)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            var followers = await _context.Follows.Where(f => f.FolloweeId == user.Id).ToListAsync();
            if (user == null || followers == null)
                return BadRequest(new
                {
                    error = true,
                    message = "Error"
                });
            List<FollowDto> followDtos = new List<FollowDto>();
            foreach (Follow follow in followers)
            {
                followDtos.Add(new FollowDto
                {
                    Follower = (await _context.Users.FirstOrDefaultAsync(u=>u.Id==follow.FollowerId)).Username
                });
            }

            string json = JsonSerializer.Serialize(followDtos);
            return Ok(new
            {
                error = false,
                message = json
            });
        }

        [HttpGet("refreshUser/{username}")]
        public async Task<ActionResult<string>> refreshUser(string username)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if(user == null)
                return BadRequest(new
                { error = true,
                    message="Error"
                });
            int numOfFollowers = (await _context.Follows.Where(f => f.FolloweeId == user.Id).ToListAsync()).Count;
            return Ok(new 
            { error=false,
                message=numOfFollowers
            });
        }

        [HttpDelete("delete/{username}")]
        public async Task<ActionResult<string>> deleteUser(string username)
        {
            User user = await _context.Users.FirstOrDefaultAsync(u=>u.Username == username);
            if (user == null)
            {
                return NotFound(new
                {
                    error = false,
                    message = "User not found"
                });
            }

            _context.Users.Remove(user);
            _context.SaveChangesAsync();
            return Ok(new
            {
                error = false,
                message = "deleted " + username
            });
        }
        private string CreateToken(User user)
        {
            List<Claim> claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, "korisnik")
            };
            var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(
                _configuration.GetSection("AppSettings:Token").Value));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);
            var token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.Now.AddDays(30),
                signingCredentials: creds
            );

            var jwt = new JwtSecurityTokenHandler().WriteToken(token);

            return jwt;
        }
        
        private string CreatePathToDataRoot(int userID, string filename)
        {
            var rootDirPath = $"../miscellaneous/avatars/{userID}";

            Directory.CreateDirectory(rootDirPath);
            
            DirectoryInfo dir = new DirectoryInfo(rootDirPath);

            foreach(FileInfo fi in dir.GetFiles())
            {
                fi.Delete();
            }

            rootDirPath = rootDirPath.Replace(@"\", "/");

            return $"{rootDirPath}/{filename}";
        }

        
    }
}
