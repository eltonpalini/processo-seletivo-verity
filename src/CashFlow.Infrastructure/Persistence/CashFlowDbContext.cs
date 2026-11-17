using CashFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Infrastructure.Persistence;

public sealed class CashFlowDbContext : DbContext
{
    public CashFlowDbContext(DbContextOptions<CashFlowDbContext> options)
        : base(options)
    {
    }

    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<DailyBalance> DailyBalances => Set<DailyBalance>();
    public DbSet<ProcessedTransaction> ProcessedTransactions => Set<ProcessedTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Transaction>(builder =>
        {
            builder.ToTable("transactions");
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            builder.Property(x => x.Amount)
                .HasColumnName("amount")
                .HasPrecision(18, 2)
                .IsRequired();

            builder.Property(x => x.Type)
                .HasColumnName("type")
                .HasConversion<int>()
                .IsRequired();

            builder.Property(x => x.Date)
                .HasColumnName("date")
                .IsRequired();

            builder.Property(x => x.Description)
                .HasColumnName("description")
                .HasMaxLength(Transaction.MaxDescriptionLength)
                .IsRequired();

            builder.Property(x => x.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .IsRequired();

            builder.HasIndex(x => x.Date)
                .HasDatabaseName("ix_transactions_date");
        });

        modelBuilder.Entity<DailyBalance>(builder =>
        {
            builder.ToTable("daily_balances");
            builder.HasKey(x => x.Date);

            builder.Property(x => x.Date)
                .HasColumnName("date")
                .ValueGeneratedNever();

            builder.Property(x => x.TotalCredits)
                .HasColumnName("total_credits")
                .HasPrecision(18, 2)
                .IsRequired();

            builder.Property(x => x.TotalDebits)
                .HasColumnName("total_debits")
                .HasPrecision(18, 2)
                .IsRequired();

            builder.Property(x => x.NetBalance)
                .HasColumnName("net_balance")
                .HasPrecision(18, 2)
                .IsRequired();

            builder.Property(x => x.TransactionCount)
                .HasColumnName("transaction_count")
                .IsRequired();

            builder.Property(x => x.LastUpdatedAtUtc)
                .HasColumnName("last_updated_at_utc")
                .IsRequired();

            builder.Property(x => x.Version)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<ProcessedTransaction>(builder =>
        {
            builder.ToTable("processed_transactions");
            builder.HasKey(x => x.TransactionId);

            builder.Property(x => x.TransactionId)
                .HasColumnName("transaction_id")
                .ValueGeneratedNever();

            builder.Property(x => x.Date)
                .HasColumnName("date")
                .IsRequired();

            builder.Property(x => x.ConsolidatedAtUtc)
                .HasColumnName("consolidated_at_utc")
                .IsRequired();

            builder.HasIndex(x => x.Date)
                .HasDatabaseName("ix_processed_transactions_date");
        });
    }
}
