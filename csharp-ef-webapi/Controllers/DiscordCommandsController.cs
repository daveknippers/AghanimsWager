#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using csharp_ef_webapi.Models;

namespace csharp_ef_webapi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DiscordCommandsController : ControllerBase
    {
        private readonly AghanimsWagerContext _context;

        public DiscordCommandsController(AghanimsWagerContext context)
        {
            _context = context;
        }

        // GET: api/MostGamesPlayed
        [HttpGet("MostGamesPlayed")]
        public async Task<ActionResult<String>> GetMostGamesPlayed()
        {
            var discordIds = await _context.DiscordIds.ToListAsync();
            var validPlayers = await _context.DiscordIds.Select(x => x.AccountId).Distinct().ToListAsync();
            var validGames = await _context.PlayerMatchDetails
                                    .Where(pmd => validPlayers.Contains(pmd.AccountId))
                                    .Join(_context.MatchStatus,
                                    matchDetail => matchDetail.MatchId,
                                    matchStatus => matchStatus.MatchId,
                                    (matchDetail, matchStatus) => new {
                                        matchDetail, matchStatus
                                    }
                                    ).Where(x => x.matchStatus.Status == 2 || x.matchStatus.Status == 3)
                                    .OrderByDescending(x => x.matchDetail.MatchId)
                                    .Take(100) // Last 100 games so it's a rolling amount
                                    .GroupBy(x => x.matchDetail.AccountId)
                                    .Select(group => new {
                                        AccountId = group.Key,
                                        GamesPlayed = group.Count(x => x.matchDetail.MatchId > 0)
                                    })
                                    .ToListAsync();
            var mostGamesPlayedAccount = validGames.Where(x => x.GamesPlayed == validGames.Max(games => games.GamesPlayed)).FirstOrDefault().AccountId;
            var playerName = discordIds.Where(di => di.AccountId == mostGamesPlayedAccount).FirstOrDefault().DiscordName;

            if(playerName == null)
            {
                playerName = "Unmapped Player";
            }
            return playerName;
        }

        // GET: api/Feederboard
        [HttpGet("Feederboard")]
        public async Task<ActionResult<IEnumerable<Feederboard>>> GetFeederboard()
        {
            List<Feederboard> feederResponse = new List<Feederboard>();
            var discordIds = await _context.DiscordIds.ToListAsync();
            var validPlayers =  await _context.DiscordIds.Select(x => x.AccountId).Distinct().ToListAsync();
            foreach(var player in validPlayers) {
                int lastNDeaths = _context.PlayerMatchDetails
                                        .Where(pmd => pmd.AccountId == player)
                                        .Join(_context.MatchStatus,
                                        matchDetail => matchDetail.MatchId,
                                        matchStatus => matchStatus.MatchId,
                                        (matchDetail, matchStatus) => new {
                                            matchDetail, matchStatus
                                        }
                                        ).Where(x => x.matchStatus.Status == 2 || x.matchStatus.Status == 3)
                                        .OrderByDescending(x => x.matchDetail.MatchId)
                                        .Take(20)
                                        .Sum( x=> x.matchDetail.Deaths.HasValue ? (int)x.matchDetail.Deaths : 0);
                var feederRow = new Feederboard();
                feederRow.Deaths = lastNDeaths;
                feederRow.DiscordName = discordIds.Where(di => di.AccountId == player).Select(di => di.DiscordName).FirstOrDefault();
                feederResponse.Add(feederRow);
            }
            feederResponse = feederResponse.OrderByDescending(x => x.Deaths).ToList();
            // Set Ranks
            for(int i=0;i<feederResponse.Count;i++){
                feederResponse[i].Rank = i+1;
            }

            return feederResponse;
        }

        // // GET: api/BalanceLedger/5
        // [HttpGet("{id}")]
        // public async Task<ActionResult<BalanceLedger>> GetBalanceLedger(string id)
        // {
        //     var balanceLedger = await _context.BalanceLedger.FindAsync(id);

        //     if (balanceLedger == null)
        //     {
        //         return NotFound();
        //     }

        //     return balanceLedger;
        // }

        // // PUT: api/BalanceLedger/5
        // // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        // [HttpPut("{id}")]
        // public async Task<IActionResult> PutBalanceLedger(long id, BalanceLedger balanceLedger)
        // {
        //     if (id != balanceLedger.DiscordId)
        //     {
        //         return BadRequest();
        //     }

        //     _context.Entry(balanceLedger).State = EntityState.Modified;

        //     try
        //     {
        //         await _context.SaveChangesAsync();
        //     }
        //     catch (DbUpdateConcurrencyException)
        //     {
        //         if (!BalanceLedgerExists(id))
        //         {
        //             return NotFound();
        //         }
        //         else
        //         {
        //             throw;
        //         }
        //     }

        //     return NoContent();
        // }

        // // POST: api/BalanceLedger
        // // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        // [HttpPost]
        // public async Task<ActionResult<BalanceLedger>> PostBalanceLedger(BalanceLedger balanceLedger)
        // {
        //     _context.BalanceLedger.Add(balanceLedger);
        //     try
        //     {
        //         await _context.SaveChangesAsync();
        //     }
        //     catch (DbUpdateException)
        //     {
        //         if (BalanceLedgerExists(balanceLedger.DiscordId))
        //         {
        //             return Conflict();
        //         }
        //         else
        //         {
        //             throw;
        //         }
        //     }

        //     return CreatedAtAction("GetBalanceLedger", new { id = balanceLedger.DiscordId }, balanceLedger);
        // }

        // // DELETE: api/BalanceLedger/5
        // [HttpDelete("{id}")]
        // public async Task<IActionResult> DeleteBalanceLedger(long id)
        // {
        //     var balanceLedger = await _context.BalanceLedger.FindAsync(id);
        //     if (balanceLedger == null)
        //     {
        //         return NotFound();
        //     }

        //     _context.BalanceLedger.Remove(balanceLedger);
        //     await _context.SaveChangesAsync();

        //     return NoContent();
        // }

        // private bool BalanceLedgerExists(long id)
        // {
        //     return _context.BalanceLedger.Any(e => e.DiscordId == id);
        // }
    }
}
