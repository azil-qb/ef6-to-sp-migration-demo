using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Data.Entity;
using EFPerformanceDemo.DBContext;
using EFPerformanceDemo.Models;

namespace EFPerformanceDemo.Utility
{
    public static class SeedData
    {
        public static async Task SeedAsync(AppDbContext context)
        {
            // Ensure database exists
            context.Database.CreateIfNotExists();

            // Use a fixed advisor GUID for seed data so it's easy to query
            var advisorFirmId = Guid.Parse("11111111-1111-1111-1111-111111111111");

            if (await context.ClientAccounts.AnyAsync())
                return; // already seeded

            var modelA = new ModelPortfolio { TargetCashAllocation = 0.10m };
            var modelB = new ModelPortfolio { TargetCashAllocation = 0.05m };

            context.ModelPortfolios.Add(modelA);
            context.ModelPortfolios.Add(modelB);

            var account1 = new ClientAccount
            {
                AdvisorFirmId = advisorFirmId,
                IsActive = true,
                AccountNumber = "ACCT-1001",
                AccountName = "Alpha Growth",
                ModelPortfolio = modelA,
                LastRebalanceTime = DateTime.UtcNow.AddDays(-30)
            };

            account1.Holdings.Add(new Holding { AssetClass = "CASH", Quantity = 1000m, CurrentPrice = 1m });
            account1.Holdings.Add(new Holding { AssetClass = "EQUITY", Quantity = 50m, CurrentPrice = 100m });

            var account2 = new ClientAccount
            {
                AdvisorFirmId = advisorFirmId,
                IsActive = true,
                AccountNumber = "ACCT-1002",
                AccountName = "Beta Income",
                ModelPortfolio = modelB,
                LastRebalanceTime = DateTime.UtcNow.AddDays(-10)
            };

            account2.Holdings.Add(new Holding { AssetClass = "CASH", Quantity = 200m, CurrentPrice = 1m });
            account2.Holdings.Add(new Holding { AssetClass = "EQUITY", Quantity = 80m, CurrentPrice = 50m });

            var account3 = new ClientAccount
            {
                AdvisorFirmId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                IsActive = true,
                AccountNumber = "ACCT-2001",
                AccountName = "Gamma Opportunity",
                ModelPortfolio = modelA,
                LastRebalanceTime = DateTime.UtcNow.AddDays(-5)
            };

            account3.Holdings.Add(new Holding { AssetClass = "CASH", Quantity = 10m, CurrentPrice = 1m });
            account3.Holdings.Add(new Holding { AssetClass = "EQUITY", Quantity = 200m, CurrentPrice = 10m });

            context.ClientAccounts.Add(account1);
            context.ClientAccounts.Add(account2);
            context.ClientAccounts.Add(account3);

            await context.SaveChangesAsync();
        }

        /// <summary>
        /// Seed data for multiple advisor firm ids. This creates <paramref name="advisorCount"/> advisors
        /// and for each inserts <paramref name="accountsPerAdvisor"/> accounts with holdings.
        /// </summary>
        public static async Task SeedLargeForAdvisorsAsync(AppDbContext context, int advisorCount = 100, int accountsPerAdvisor = 1000, int holdingsPerAccount = 10, int batchSize = 1000, bool force = false)
        {
            context.Database.CreateIfNotExists();

            if (!force && await context.ClientAccounts.AnyAsync())
                return;

            var rand = new Random(123);

            // Ensure there are some portfolios available
            var portfolios = await context.ModelPortfolios.Take(5).ToListAsync();
            if (portfolios.Count == 0)
            {
                portfolios = new List<ModelPortfolio>
                {
                    new ModelPortfolio { TargetCashAllocation = 0.00m },
                    new ModelPortfolio { TargetCashAllocation = 0.05m },
                    new ModelPortfolio { TargetCashAllocation = 0.10m },
                    new ModelPortfolio { TargetCashAllocation = 0.15m },
                    new ModelPortfolio { TargetCashAllocation = 0.20m }
                };
                context.ModelPortfolios.AddRange(portfolios);
                await context.SaveChangesAsync();
            }

            var totalAccounts = advisorCount * accountsPerAdvisor;

            // optimize EF for bulk operations
            var prevAutoDetect = context.Configuration.AutoDetectChangesEnabled;
            var prevValidate = context.Configuration.ValidateOnSaveEnabled;
            try
            {
                context.Configuration.AutoDetectChangesEnabled = false;
                context.Configuration.ValidateOnSaveEnabled = false;

                var batchList = new List<ClientAccount>(batchSize);
                int created = 0;

                for (int a = 0; a < advisorCount; a++)
                {
                    var advisorId = Guid.NewGuid();

                    for (int i = 0; i < accountsPerAdvisor; i++)
                    {
                        var idx = a * accountsPerAdvisor + i + 1;
                        var acct = new ClientAccount
                        {
                            AdvisorFirmId = advisorId,
                            IsActive = true,
                            AccountNumber = $"ADV-{a + 1}-ACCT-{i + 1}",
                            AccountName = $"Advisor {a + 1} Account {i + 1}",
                            ModelPortfolio = portfolios[rand.Next(portfolios.Count)],
                            LastRebalanceTime = DateTime.UtcNow.AddDays(-rand.Next(0, 365))
                        };

                        for (int h = 0; h < holdingsPerAccount; h++)
                        {
                            var assetClass = (h == 0) ? "CASH" : (rand.NextDouble() < 0.2 ? "CASH" : "EQUITY");
                            var quantity = (decimal)rand.Next(1, 1000);
                            var price = (decimal)(rand.NextDouble() * 200.0 + 1.0);
                            acct.Holdings.Add(new Holding { AssetClass = assetClass, Quantity = quantity, CurrentPrice = Math.Round(price, 2) });
                        }

                        batchList.Add(acct);
                        created++;

                        if (batchList.Count >= batchSize)
                        {
                            context.ClientAccounts.AddRange(batchList);
                            await context.SaveChangesAsync();

                            var entries = context.ChangeTracker.Entries().ToList();
                            foreach (var e in entries)
                                e.State = EntityState.Detached;

                            batchList.Clear();
                        }
                    }
                }

                if (batchList.Count > 0)
                {
                    context.ClientAccounts.AddRange(batchList);
                    await context.SaveChangesAsync();
                    batchList.Clear();
                }
            }
            finally
            {
                context.Configuration.AutoDetectChangesEnabled = prevAutoDetect;
                context.Configuration.ValidateOnSaveEnabled = prevValidate;
            }
        }

        /// <summary>
        /// Seeds a very large number of ClientAccount/Holding rows for load testing.
        /// This method is optimized for bulk insert by batching and disabling change-tracking.
        /// </summary>
        public static async Task SeedLargeAsync(AppDbContext context, int numAccounts = 20000, int holdingsPerAccount = 10, int batchSize = 1000, bool force = false)
        {
            // Ensure database exists
            context.Database.CreateIfNotExists();

            if (!force && await context.ClientAccounts.AnyAsync())
                return; // avoid duplicating data unless forced

            var advisorFirmId = Guid.Parse("11111111-1111-1111-1111-111111111111");

            // Prepare a small set of model portfolios to reuse across accounts
            var portfolios = new List<ModelPortfolio>
            {
                new ModelPortfolio { TargetCashAllocation = 0.00m },
                new ModelPortfolio { TargetCashAllocation = 0.05m },
                new ModelPortfolio { TargetCashAllocation = 0.10m },
                new ModelPortfolio { TargetCashAllocation = 0.15m },
                new ModelPortfolio { TargetCashAllocation = 0.20m }
            };

            // Add portfolios if none exist
            if (!await context.ModelPortfolios.AnyAsync())
            {
                context.ModelPortfolios.AddRange(portfolios);
                await context.SaveChangesAsync();
            }
            else
            {
                portfolios = await context.ModelPortfolios.Take(5).ToListAsync();
                if (portfolios.Count == 0)
                {
                    portfolios = new List<ModelPortfolio>
                    {
                        new ModelPortfolio { TargetCashAllocation = 0.05m }
                    };
                    context.ModelPortfolios.AddRange(portfolios);
                    await context.SaveChangesAsync();
                }
            }

            var rand = new Random(42);

            // Speed up bulk inserts
            var prevAutoDetect = context.Configuration.AutoDetectChangesEnabled;
            var prevValidate = context.Configuration.ValidateOnSaveEnabled;
            try
            {
                context.Configuration.AutoDetectChangesEnabled = false;
                context.Configuration.ValidateOnSaveEnabled = false;

                var toInsert = new List<ClientAccount>(batchSize);
                for (int i = 1; i <= numAccounts; i++)
                {
                    var acct = new ClientAccount
                    {
                        AdvisorFirmId = advisorFirmId,
                        IsActive = true,
                        AccountNumber = $"ACCT-{i:D8}",
                        AccountName = $"Seeded Account {i}",
                        ModelPortfolio = portfolios[rand.Next(portfolios.Count)],
                        LastRebalanceTime = DateTime.UtcNow.AddDays(-rand.Next(0, 365))
                    };

                    // create holdings
                    for (int h = 0; h < holdingsPerAccount; h++)
                    {
                        // Make some holdings CASH to exercise cash-weight calculations
                        var assetClass = (h == 0) ? "CASH" : (rand.NextDouble() < 0.2 ? "CASH" : "EQUITY");
                        var quantity = (decimal)(rand.Next(1, 1000));
                        var price = (decimal)(rand.NextDouble() * 200.0 + 1.0);
                        acct.Holdings.Add(new Holding { AssetClass = assetClass, Quantity = quantity, CurrentPrice = Math.Round(price, 2) });
                    }

                    toInsert.Add(acct);

                    if (toInsert.Count >= batchSize || i == numAccounts)
                    {
                        context.ClientAccounts.AddRange(toInsert);
                        await context.SaveChangesAsync();

                        // Detach tracked entities to keep memory small
                        var entries = context.ChangeTracker.Entries().ToList();
                        foreach (var e in entries)
                            e.State = EntityState.Detached;

                        toInsert.Clear();
                    }
                }
            }
            finally
            {
                context.Configuration.AutoDetectChangesEnabled = prevAutoDetect;
                context.Configuration.ValidateOnSaveEnabled = prevValidate;
            }
        }
    }
}
