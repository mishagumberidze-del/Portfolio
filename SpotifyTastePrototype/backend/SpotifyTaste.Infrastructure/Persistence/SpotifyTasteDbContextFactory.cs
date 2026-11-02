using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SpotifyTaste.Infrastructure.Persistence;

public sealed class SpotifyTasteDbContextFactory : IDesignTimeDbContextFactory<SpotifyTasteDbContext>
{
    public SpotifyTasteDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SpotifyTasteDbContext>()
            .UseSqlServer("Server=.\\SQLEXPRESS;Database=SpotifyTaste;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True")
            .Options;
        return new SpotifyTasteDbContext(options);
    }
}
