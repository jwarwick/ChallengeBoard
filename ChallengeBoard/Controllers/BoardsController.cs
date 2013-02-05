﻿using System;
using System.Linq;
using System.Web.Mvc;
using System.Web.WebPages;
using ChallengeBoard.Models;
using ChallengeBoard.Services;

namespace ChallengeBoard.Controllers
{
    public class BoardsController : Controller
    {
        private readonly IRepository _repositiory;
        private readonly IBoardService _boardService;

        public BoardsController(IRepository repository, IBoardService boardService)
        {
            _repositiory = repository;
            _boardService = boardService;
        }

        //
        // GET: /Boards/

        public ActionResult Index(string user = "", int page = 1)
        {
            var boards = _repositiory.Boards;

            if (!user.IsEmpty())
            {
                var profile = _repositiory.UserProfiles.FindProfile(User.Identity.Name);
                boards =
                    boards.Where(
                        x =>
                        x.Competitors.Any(y => y.ProfileUserId == profile.UserId && y.Status == CompetitorStatus.Active));
            }
            else
                boards = boards.Where(x => x.End > DateTime.Now); // Hide expired boards if not looking at "yours"

            // Persist any status messages across redirection.
            ViewBag.StatusMessage = TempData["StatusMessage"];

            return View(boards.OrderByDescending(x => x.End).Skip(Math.Abs(page-1)).Take(50));
        }

        //
        // GET: /Boards/Details/5

        public ActionResult Details(int id = 0)
        {
            var board = _repositiory.GetBoardByIdWithCompetitors(id);
            
            if (board == null)
                return View("BoardNotFound");
            
            return View(board);
        }

        //
        // GET: /Boards/Create

        [Authorize]
        public ActionResult Create()
        {
            return View(new Board());
        }

        //
        // POST: /Boards/Create

        [HttpPost]
        [Authorize]
        public ActionResult Create(Board board)
        {
            if (ModelState.IsValid)
            {
                var owner = new Competitor
                {
                    Name = User.Identity.Name,
                    Rating = board.StartingRating,
                    Profile = _repositiory.UserProfiles.FindProfile(User.Identity.Name)
                };

                _repositiory.Add(owner);
                _repositiory.CommitChanges();

                board.Created = DateTime.Now;
                board.Started = DateTime.Now;
                board.Owner = owner;
                board.Competitors.Add(owner);

                _repositiory.Add(board);
                _repositiory.CommitChanges();

                return RedirectToAction("Index");
            }

            return View(board);
        }

        // 
        // GET: /Boards/Instructions/5
        [Authorize]
        public ActionResult Instructions(int id = 0)
        {
            var existingBoard = _repositiory.GetBoardByIdWithCompetitors(id);

            if (existingBoard == null)
                return View("BoardNotFound");

            return View("Instructions", existingBoard);
        }

        // 
        // GET: /Boards/Join/1
        [Authorize]
        public ActionResult Join(int id = 0)
        {
            var existingBoard = _repositiory.GetBoardByIdWithCompetitors(id);

            if (existingBoard == null)
                return View("BoardNotFound");

            if (existingBoard.Password.IsEmpty() ||
                existingBoard.Competitors.Active().ContainsCompetitor(User.Identity.Name))
                return RedirectToAction("Details", new { id = existingBoard.BoardId });

            return View("Join", existingBoard);
        }

        // 
        // POST: /Boards/Join/5
        [HttpPost, ActionName("Join")]
        [Authorize]
        public ActionResult JoinBoard(int id, string password = "")
        {
            // TODO, move this into a BoardService
            var existingBoard = _repositiory.GetBoardByIdWithCompetitors(id);

            // Password failure
            if (!existingBoard.Password.IsEmpty() && 
                !existingBoard.Password.Equals(password, StringComparison.InvariantCultureIgnoreCase))
            {
                ModelState.AddModelError("invalidPassword", "The password you entered was incorrect");
                return View("Join", existingBoard);
            }

            var competitor = existingBoard.Competitors.FindCompetitor(User.Identity.Name);

            if (competitor == null) // New
            {
                existingBoard.Competitors.Add(new Competitor
                {
                    Name = User.Identity.Name,
                    Rating = existingBoard.StartingRating,
                    Profile = _repositiory.UserProfiles.FindProfile(User.Identity.Name)
                });
            }
            else if (competitor.Status == CompetitorStatus.Retired) // Retired
                competitor.Status = CompetitorStatus.Active;
            else
                return View("Banned", existingBoard); // Banned

            _repositiory.CommitChanges();

            return RedirectToAction("Instructions", new { id = existingBoard.BoardId });
        }

        //
        // GET: /Boards/Edit/5

        [Authorize]
        public ActionResult Edit(int id = 0)
        {
            var board = _repositiory.GetBoardByIdWithCompetitors(id);
            
            if (board == null)
                return View("BoardNotFound");

            if (board.Owner.Name != User.Identity.Name)
                return View("InvalidOwner", board);

            return View(board);
        }

        //
        // POST: /Boards/Edit/5

        [HttpPost]
        //public ActionResult Edit(int id, FormCollection formValues)
        public ActionResult Edit(int id, Board userBoard)
        {
            if (ModelState.IsValid)
            {
                var board = _repositiory.GetBoardById(id);

                if (board.Owner.Name != User.Identity.Name)
                    return View("InvalidOwner", board);

                if (userBoard.AutoVerification != board.AutoVerification)
                {
                    userBoard.Matches = _repositiory.GetUnresolvedMatchesByBoardId(userBoard.BoardId).ToList();
                    _boardService.AdjustMatchDeadlines(userBoard);
                }

                UpdateModel(board);

                _repositiory.CommitChanges();
                
                return RedirectToAction("Details", new { id = userBoard.BoardId });
            }

            return View(userBoard);
        }

        //
        // GET: /Boards/Delete/5

        [Authorize]
        public ActionResult Delete(int id = 0)
        {
            var board = _repositiory.GetBoardById(id);

            if(_repositiory.Matches.Any(x => x.Board.BoardId == id))
                return View("CanNotDelete", board);

            if (board == null)
                return View("BoardNotFound");

            if (board.Owner.Name != User.Identity.Name)
                return View("InvalidOwner", board);

            return View(board);
        }

        //
        // POST: /Boards/Delete/5

        [HttpPost, ActionName("Delete")]
        [Authorize]
        public ActionResult DeleteConfirmed(int id)
        {
            var board = _repositiory.GetBoardById(id);

            if (_repositiory.Matches.Any(x => x.Board.BoardId == id))
                return View("CanNotDelete", board);

            if (board == null)
                return View("BoardNotFound");

            if (board.Owner.Name != User.Identity.Name)
                return View("InvalidOwner", board);

            board.Owner = null;
            _repositiory.CommitChanges();

            _repositiory.Delete(board);
            _repositiory.CommitChanges();

            return RedirectToAction("Index");
        }

        
        [Authorize]
        public ActionResult Retire(int id)
        {
            var existingBoard = _repositiory.GetBoardByIdWithCompetitors(id);

            if (existingBoard == null)
                return View("BoardNotFound");

            if (existingBoard.Competitors.Active().ContainsCompetitor(User.Identity.Name))
                return View("Retire", existingBoard);

            return RedirectToAction("Index");
        }

        // 
        // POST: /Boards/Retire/1
        [HttpPost, ActionName("Retire")]
        [Authorize]
        public ActionResult RetireConfirmed(int id)
        {
            var existingBoard = _repositiory.GetBoardByIdWithCompetitors(id);

            var competitor = existingBoard.Competitors.Active().FirstOrDefault(x => x.Name == User.Identity.Name);

            if(competitor == null)
                return RedirectToAction("Index");

            if (competitor.Name == existingBoard.Owner.Name)
                return View("Unleavable", existingBoard);

            if (competitor.Status == CompetitorStatus.Active)
            {
                competitor.Status = CompetitorStatus.Retired;
                _repositiory.CommitChanges();
            }

            // Rejection message persisted across redirection.
            TempData["StatusMessage"]  = String.Format("You have retired from {0}", existingBoard.Name);

            return RedirectToAction("Index");
        }

        //
        // Get: /Boards/Standings/5

        public ActionResult Standings(int id = 0)
        {
            var existingBoard = _repositiory.GetBoardByIdWithCompetitors(id);

            if (existingBoard == null)
                return View("BoardNotFound");

            return View("Standings", existingBoard);
        }
    }
}