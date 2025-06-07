using BulbaLib.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<Novel> Novels { get; set; }
    // Аналогично для других моделей (Users, Chapters и т.д.)
}
