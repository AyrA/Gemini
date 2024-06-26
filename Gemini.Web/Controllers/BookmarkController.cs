﻿using Gemini.Web.Exceptions;
using Gemini.Web.Models;
using Gemini.Web.Services;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Gemini.Web.Controllers
{
    [ApiController, Route("[controller]/[action]/{id}"), EnableCors("API")]
    public class BookmarkController(BookmarkService bookmarks) : Controller
    {

        /// <summary>
        /// Lists all bookmarks
        /// </summary>
        /// <returns>Bookmarks</returns>
        [HttpGet, Produces("application/json"), Route("/[controller]/[action]")]
        public BookmarkViewModel[] List()
        {
            return bookmarks.Ids.Select(m => new BookmarkViewModel(m, bookmarks[m])).ToArray();
        }

        /// <summary>
        /// Gets a single bookmark
        /// </summary>
        /// <param name="id">Bookmark id</param>
        /// <returns>Bookmark</returns>
        [HttpGet, ActionName("Bookmark"), Produces("application/json", Type = typeof(BookmarkModel))]
        public IActionResult BookmarkGet(Guid id)
        {
            try
            {
                return Json(bookmarks[id]);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        /// <summary>
        /// Deletes a bookmark
        /// </summary>
        /// <param name="id">Bookmark id</param>
        /// <returns>Deleted bookmark</returns>
        [HttpDelete, ActionName("Bookmark"), Produces("application/json", Type = typeof(BookmarkModel))]
        public IActionResult BookmarkDelete(Guid id)
        {
            BookmarkModel model;
            try
            {
                model = bookmarks[id];
            }
            catch
            {
                return NotFound(id);
            }
            if (bookmarks.Delete(id))
            {
                bookmarks.Save();
                return Json(model);
            }
            return NotFound();
        }

        /// <summary>
        /// Adds a new bookmark
        /// </summary>
        /// <param name="name">Name</param>
        /// <param name="url">Gemini URL</param>
        /// <returns>New bookmark id</returns>
        [HttpPut]
        [ActionName("Bookmark")]
        [Route("/[controller]/[action]")]
        [Produces("application/json", Type = typeof(Guid))]
        public IActionResult BookmarkPut([FromForm] string name, [FromForm] Uri url)
        {
            try
            {
                var id = bookmarks.Add(new BookmarkModel(name, url));
                bookmarks.Save();
                return Json(id);
            }
            catch (ValidationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        /// <summary>
        /// Updates a bookmark
        /// </summary>
        /// <param name="id">Bookmark id</param>
        /// <param name="name">New name</param>
        /// <param name="url">New Gemini URL</param>
        /// <returns>Updated bookmark</returns>
        [HttpPatch, ActionName("Bookmark"), Produces("application/json", Type = typeof(BookmarkModel))]
        public IActionResult BookmarkPatch(Guid id, [FromForm] string name, [FromForm] Uri url)
        {
            try
            {
                var bm = new BookmarkModel(name, url);
                bookmarks.Update(id, bm);
                bookmarks.Save();
                return Json(bm);
            }
            catch (KeyNotFoundException)
            {
                //Cannot delete existing key
                return NotFound();
            }
            catch (ValidationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}
