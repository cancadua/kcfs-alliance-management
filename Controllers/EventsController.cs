using AllianceRewards.Api.Data;
using AllianceRewards.Api.DTOs;
using AllianceRewards.Api.Models;
using AllianceRewards.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AllianceRewards.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/events")]
public class EventsController(AppDbContext db, AllianceAccessService access) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<EventResponse>>> List([FromQuery] Guid? allianceId)
    {
        var q = db.Events.Where(e => access.ManagedAllianceIds().Contains(e.AllianceId));
        if (allianceId is not null) q = q.Where(e => e.AllianceId == allianceId);

        return await q.OrderByDescending(e => e.Date)
            .Select(e => new EventResponse(e.Id, e.AllianceId, e.Name, e.Description, e.Date))
            .ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<EventResponse>> Create(CreateEventRequest req)
    {
        if (!await access.IsManagerAsync(req.AllianceId)) return NotFound(new { error = "Alliance not found." });

        var ev = new Event
        {
            AllianceId = req.AllianceId,
            Name = req.Name.Trim(),
            Description = req.Description,
            Date = (req.Date ?? DateTime.UtcNow).ToUniversalTime(),
        };
        db.Events.Add(ev);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = ev.Id },
            new EventResponse(ev.Id, ev.AllianceId, ev.Name, ev.Description, ev.Date));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EventResponse>> Get(Guid id)
    {
        var ev = await db.Events
            .Where(e => e.Id == id && access.ManagedAllianceIds().Contains(e.AllianceId))
            .Select(e => new EventResponse(e.Id, e.AllianceId, e.Name, e.Description, e.Date))
            .FirstOrDefaultAsync();
        return ev is null ? NotFound() : ev;
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<EventResponse>> Update(Guid id, UpdateEventRequest req)
    {
        var ev = await FindOwnedAsync(id);
        if (ev is null) return NotFound();

        if (req.Name is not null) ev.Name = req.Name.Trim();
        if (req.Description is not null) ev.Description = req.Description;
        if (req.Date is not null) ev.Date = req.Date.Value.ToUniversalTime();

        await db.SaveChangesAsync();
        return new EventResponse(ev.Id, ev.AllianceId, ev.Name, ev.Description, ev.Date);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var ev = await FindOwnedAsync(id);
        if (ev is null) return NotFound();

        db.Events.Remove(ev);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private Task<Event?> FindOwnedAsync(Guid id) =>
        db.Events.FirstOrDefaultAsync(e => e.Id == id && access.ManagedAllianceIds().Contains(e.AllianceId));
}
