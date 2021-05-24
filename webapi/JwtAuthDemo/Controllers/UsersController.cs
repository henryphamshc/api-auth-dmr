using JwtAuthDemo.Data;
using JwtAuthDemo.Dtos;
using JwtAuthDemo.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace JwtAuthDemo.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]/[action]")]
    public class UsersController : Controller
    {
        private readonly DataContext _context;

        public UsersController(DataContext context)
        {
            _context = context;
        }
        private void CreatePasswordHash(string password, out byte[] passwordHash, out byte[] passwordSalt)
        {
            using var hmac = new System.Security.Cryptography.HMACSHA512();
            passwordSalt = hmac.Key;
            passwordHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
        }
        [HttpDelete]
        public async Task<IActionResult> Delete(int id)
        {
            var entity = await _context.Users.FindAsync(id);
            if (entity == null)
            {
                return NotFound();
            }

            try
            {
                entity.IsShow = false;
                _context.Users.Update(entity);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        [HttpPost]
        public async Task<IActionResult> Create(UpdateUserDto entity)
        {
            var item = await _context.Users.FirstOrDefaultAsync(x => x.EmployeeID.ToLower().Equals(entity.EmployeeID.ToLower()));
            if (item == null)
            {
                byte[] passwordHash, passwordSalt;
                CreatePasswordHash(entity.Password, out passwordHash, out passwordSalt);
                var user = new User
                {
                    Username = entity.Username,
                    Email = entity.Email,
                    EmployeeID = entity.EmployeeID,
                    IsShow = true,
                    LevelOC = entity.LevelOC,
                    OCID = entity.OCID,
                    RoleID = entity.RoleID
                };
                user.PasswordHash = passwordHash;
                user.PasswordSalt = passwordSalt;
                user.ModifyTime = DateTime.Now;
                user.IsShow = true;
                await _context.Users.AddAsync(user);
                try
                {
                    await _context.SaveChangesAsync();
                    _context.UserSystems.Add(new UserSystem
                    {
                        UserID = user.ID,
                        SystemID = entity.SystemCode,
                        Status = true,
                        DateTime = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();
                    return NoContent();
                }
                catch (Exception ex)
                {
                    return BadRequest(ex.Message);
                }
            }
            else
            {
                if (item.IsShow == false)
                {
                    item.IsShow = true;
                    _context.Update(item);
                    await _context.SaveChangesAsync();
                }
                var userSystem = await _context.UserSystems.FirstOrDefaultAsync(x => x.UserID == item.ID && x.SystemID == entity.SystemCode);
                if (userSystem == null)
                {
                    _context.UserSystems.Add(new UserSystem
                    {
                        UserID = item.ID,
                        SystemID = entity.SystemCode,
                        Status = true,
                        DateTime = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();
                    return NoContent();
                }
                else
                {
                    
                    try
                    {
                        _context.Remove(userSystem);
                        await _context.SaveChangesAsync();
                        return Ok();
                    }
                    catch (Exception ex)
                    {
                        return BadRequest(ex.Message);
                    }
                }
            }

        }
        [HttpPost]
        public async Task<IActionResult> Update(UpdateUserDto entity)
        {
            var item = await _context.Users.FindAsync(entity.ID);
            item.EmployeeID = entity.EmployeeID;
            item.Username = entity.Username;
            item.Email = entity.Email;
            item.ModifyTime = DateTime.Now;
            item.RoleID = 2;
            if (!string.IsNullOrEmpty(entity.Password))
            {
                byte[] passwordHash, passwordSalt;
                CreatePasswordHash(entity.Password, out passwordHash, out passwordSalt);
                item.PasswordHash = passwordHash;
                item.PasswordSalt = passwordSalt;
            }

            item.ModifyTime = DateTime.Now;

            try
            {
                _context.Users.Update(item);
                await _context.SaveChangesAsync();
                return NoContent();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{page}/{pageSize}/{keyword}")]
        [HttpGet("{page}/{pageSize}")]

        public async Task<ActionResult> GetAllPaging(int page, int pageSize, string keyword = "")
        {
            var model = await GetAll(page, pageSize, keyword);
            Response.AddPagination(model.CurrentPage, model.PageSize, model.TotalCount, model.TotalPages);
            return Ok(new
            {
                data = model,
                total = model.TotalPages,
                page,
                pageSize
            });
        }
        async Task<PagedList<UserDto>> GetAll(int page, int pageSize, string keyword)
        {
            var source = _context.Users.Include(x => x.UserSystems)
                                .Where(x => x.IsShow == true && x.Username != "admin")
                                .OrderByDescending(x => x.ID)
                                .Select(x => new UserDto {
                                    isLeader = x.isLeader, 
                                    ID = x.ID, 
                                    Username = x.Username, 
                                    Email = x.Email, 
                                    RoleName = x.Role.Name, 
                                    RoleID = x.RoleID, 
                                    EmployeeID = x.EmployeeID })
                                .AsQueryable();
            if (!string.IsNullOrEmpty(keyword))
            {
                source = source.Where(x => x.Username.Contains(keyword) || x.Email.Contains(keyword));
            }
            return await PagedList<UserDto>.CreateAsync(source, page, pageSize);
        }

        [HttpGet("{systemCode}/{page}/{pageSize}")]
        [HttpGet("{systemCode}/{page}/{pageSize}/{keyword}")]
        public async Task<ActionResult> GetAllUsers(int systemCode, int page, int pageSize, string keyword = "")
        {
            var model = await GetAllPaging(systemCode, page, pageSize, keyword);
            Response.AddPagination(model.CurrentPage, model.PageSize, model.TotalCount, model.TotalPages);
            return Ok(model);
        }
         async Task<PagedList<UserDto>> GetAllPaging(int systemCode, int page, int pageSize, string keyword)
        {
            var source = _context.Users
                .Include(x => x.UserSystems)
                .Where(x => x.IsShow == true && x.Username != "admin" && x.UserSystems
                .Where(x => x.Status == true)
                .Select(x => x.SystemID)
                .Contains(systemCode))
                .OrderByDescending(x => x.ID)
                .Select(x => new UserDto { 
                    isLeader = x.isLeader,
                    ID = x.ID, 
                    Username = x.Username, 
                    Email = x.Email, 
                    RoleName = x.Role.Name, 
                    RoleID = x.RoleID, 
                    EmployeeID = x.EmployeeID })
                .AsQueryable();
            if (!string.IsNullOrEmpty(keyword))
            {
                source = source.Where(x => x.Username.Contains(keyword) || x.Email.Contains(keyword));
            }
            return await PagedList<UserDto>.CreateAsync(source, page, pageSize);
        }
    }
}
