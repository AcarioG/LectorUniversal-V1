﻿using AutoMapper;
using LectorUniversal.Server.Data;
using LectorUniversal.Server.Helpers;
using LectorUniversal.Server.Models;
using LectorUniversal.Shared;
using LectorUniversal.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LectorUniversal.Server.Controllers
{
    //[Authorize(Roles = "admin")]
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class BooksController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IFileUpload _fileUpload;
        private readonly IMapper _mapper;
        private readonly UserManager<ApplicationUser> _userManager;

        public BooksController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IFileUpload fileUpload,
            IMapper mapper)
        {
            _db = db;
            _fileUpload = fileUpload;
            _mapper = mapper;
            _userManager = userManager;
        }


        [HttpGet("home")]
        [AllowAnonymous]
        public async Task<ActionResult<List<Book>>> GetNewest()
        {
            //List<Book> book = new List<Book>();
            //var books = String.Format("SELECT T1.Name, T1.Cover, MAX(T0.CreatedOn) FROM Chapters T0 INNER JOIN Books T1 ON T0.BooksId = T1.Id GROUP BY T1.Name,T0.BooksId, T1.Cover ORDER BY MAX(T1.CreatedOn) DESC;");

            //var results = Task.Run(() => _db.Database.ExecuteSqlInterpolatedAsync($"SELECT T0.* FROM [UltimosComics] T0 GROUP BY T0.NombreComic, T0.Cover, T0.FechaChapter ORDER BY MAX(T0.FechaChapter) DESC")).GetAwaiter().GetResult();
            //var chapters = await _db.Chapters.Distinct().OrderByDescending(x => x.CreatedOn).ToListAsync();
            var books = await _db.Books.Include(x => x.Chapters).OrderByDescending(x => x.CreatedOn).Take(12).ToListAsync();
            //var books = await _db.Books.ToListAsync();
            //var books = results.OrderByDescending(x => x.Chapters.Max(x =>x.CreatedOn)).ToList();
            //var model = new VisualiseBookDTO();

            //model.Chapters = chapters;
            //model.Book = books.FirstOrDefault();

            return books;
        }

        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<ActionResult<VisualiseBookDTO>> Get(int id)
        {
            var Book = await _db.Books.Where(x => x.Id == id)
                .Include(x => x.Genders).ThenInclude(x => x.Gender)
                .Include(x => x.Chapters.Where(c => c.BooksId == id))
                .FirstOrDefaultAsync();

            if (Book == null) { return NotFound(); } 

            var averageVote = 0.0;
            var userVote = 0;

            if(await _db.BookVotes.AnyAsync(x => x.BookId == id))
            {
                //Get the average votes from a comic
                averageVote = await _db.BookVotes.Where(x => x.BookId == id).AverageAsync(x => x.Vote);

                //Get the vote from user if the user is Authenticated
                if (HttpContext.User.Identity.IsAuthenticated)
                {
                    var user = await _userManager.FindByNameAsync(HttpContext.User.Identity.Name);
                    var userId = user.Id;

                    var userDBVote = await _db.BookVotes.FirstOrDefaultAsync(x => x.BookId == id && x.UserId == userId);

                    if (userDBVote != null)
                    {
                        userVote = userDBVote.Vote;
                    }
                }
            }

            var model = new VisualiseBookDTO();
            model.Book = Book;
            model.Genders = Book.Genders.Select(x => x.Gender).ToList();
            model.Chapters = Book.Chapters.ToList();
            model.AverageVote = averageVote;
            model.UserVote = userVote;

            return model;
        }

        
        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<List<Book>>> GetAll([FromQuery]PaginationDTO pagination)
        {
            var queryable = _db.Books.AsQueryable();
            await HttpContext.InsertParameterInResponse(queryable, pagination.Records);
            return await queryable.Pagination(pagination).ToListAsync();
        }

        [HttpGet("update/{id}")]
        public async Task<ActionResult<BookUpdateDTO>> PutGet(int id)
        {
            var bookActionResult = await Get(id);
            if (bookActionResult.Result is NotFoundResult) { return NotFound(); }

            var bookViewDTO = bookActionResult.Value;
            var gendersSelectedId = bookViewDTO.Genders.Select(x => x.Id).ToList();
            var genedersNotSelected = await _db.Genders.Where(x => !gendersSelectedId.Contains(x.Id)).ToListAsync();

            var model = new BookUpdateDTO();
            model.Book = bookViewDTO.Book;
            model.GendersNotSelected = genedersNotSelected;
            model.GendersSelected = bookViewDTO.Genders;
            return model;
        }

        [HttpPost]
        public async Task<ActionResult<int>> Post([FromBody] Book book)
        {
            //Create directory path to save the images
            if (!string.IsNullOrWhiteSpace(book.Cover))
            {
                string folder = $"{book.Name.Replace(" ", "-").Replace(":", "").Replace("#", "")}";
                var coverPoster = Convert.FromBase64String(book.Cover);
                var bookType = Enum.GetName(book.TypeofBook);
                book.Cover = await _fileUpload.SaveFile(coverPoster, "jpg",bookType, folder);
            }

            _db.Add(book);
            await _db.SaveChangesAsync();
            return book.Id;
        }

        [HttpPut]
        public async Task<ActionResult> Put(Book book)
        {
            var bookDB = await _db.Books.AsNoTracking().FirstOrDefaultAsync(x => x.Id == book.Id);

            if (bookDB == null) { return NotFound(); }

            //Edit the local path to save the images
            if (!string.IsNullOrWhiteSpace(book.Cover))
            {
                var coverImage = Convert.FromBase64String(book.Cover);
                var actualfolder = $"{bookDB.Name.Replace(" ", "-")}";
                var newfolder = $"{book.Name.Replace(" ", "-").Replace(":", "").Replace("#", "")}";
                var bookType = Enum.GetName(bookDB.TypeofBook);
                bool complete = false;
                bookDB.Cover = await _fileUpload.EditFile(coverImage, "jpg", actualfolder, newfolder, bookDB.Cover, bookType, complete);
            }

            bookDB = _mapper.Map(book, bookDB);
            
            await _db.Database.ExecuteSqlInterpolatedAsync($"delete from GenderBooks WHERE BookId = {book.Id};");

            bookDB.Genders = book.Genders;

            _db.Attach(bookDB).State = EntityState.Modified;
            await _db.GenderBooks.AddRangeAsync(bookDB.Genders);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(int id)
        {
            var exits = await _db.Books.AsNoTracking().AnyAsync(x => x.Id == id);
            if (!exits) { return NotFound(); }

            //Delete the local path completely
            var book = await _db.Books.AsNoTracking().Where(x => x.Id == id).FirstOrDefaultAsync();
            var folder = book.Name.Replace(" ", "-");
            var bookType = Enum.GetName(book.TypeofBook);
            bool complete = true;
            await _fileUpload.DeleteFile(folder,bookType, book.Cover, complete);

            _db.Remove(new Book { Id = id });
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
