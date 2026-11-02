using Microsoft.AspNetCore.Mvc;
using SpotifyTaste.Application;
using SpotifyTaste.Infrastructure.Recommendations;
namespace SpotifyTaste.Api.Controllers;

[ApiController, Route("api/feedback")]
public sealed class FeedbackController(FeedbackService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<FeedbackDto>> Add(FeedbackRequest request, CancellationToken ct)
    {
        try { return Ok(await service.AddAsync(request, ct)); }
        catch (ArgumentException e) { return BadRequest(e.Message); }
        catch (KeyNotFoundException e) { return NotFound(e.Message); }
    }
}
[ApiController, Route("api/evaluation")]
public sealed class EvaluationController(FeedbackService service, RecommendationWorkGate gate) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => new JsonResult(await service.GetAsync(ct));
    [HttpPost("start")]
    public Task<IActionResult> Start([FromQuery] int target = 50, CancellationToken ct = default) => Mutate(() => service.StartAsync(target, ct), ct);
    [HttpPost("next")]
    public Task<IActionResult> Next(CancellationToken ct) => Mutate(() => service.NextAsync(ct), ct);
    private async Task<IActionResult> Mutate(Func<Task<EvaluationDto>> action, CancellationToken ct)
    {
        await gate.Gate.WaitAsync(ct);
        try { return Ok(await action()); }
        catch (ArgumentException e) { return BadRequest(e.Message); }
        finally { gate.Gate.Release(); }
    }
}
