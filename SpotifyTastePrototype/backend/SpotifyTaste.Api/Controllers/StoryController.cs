using Microsoft.AspNetCore.Mvc;
using SpotifyTaste.Application;
namespace SpotifyTaste.Api.Controllers;

[ApiController, Route("api/story")]
public sealed class StoryController(IStoryService stories) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<StoryDto>> Get([FromQuery] int? year, CancellationToken cancellationToken)
    {
        if (year is < 1900 or > 9998) return BadRequest("Year must be between 1900 and 9998.");
        return Ok(await stories.GetAsync(year, cancellationToken));
    }
}
