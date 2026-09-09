using Microsoft.EntityFrameworkCore;
using PQM.Core.Entities;

namespace PQM.Infrastructure
{
    public class DataContext : DbContext
    {
        private readonly string? _connectionString;
        public DbSet<User> User { get; set; } = null!;
        public DbSet<Device> Device { get; set; } = null!;
        public DbSet<DeviceSyncSchedule> DeviceSyncSchedules { get; set; } = null!;
        public DbSet<MeterType> MeterType { get; set; } = null!;
        public DbSet<Profile> Profiles { get; set; } = null!;
        public DbSet<Parameter> Parameter { get; set; } = null!;

        public DbSet<ReadingSession> ReadingSessions { get; set; } = null!;
        public DbSet<ReadingValue> ReadingValues { get; set; } = null!;
        public DbSet<DeviceProfileSyncState> DeviceProfileSyncStates { get; set; } = null!;
        public DbSet<DeviceLatestReading> DeviceLatestReadings { get; set; } = null!;
        public DbSet<DeviceEvent> DeviceEvents { get; set; } = null!;
        public DbSet<DeviceSyncHistory> DeviceSyncHistories { get; set; } = null!;
        

        public DataContext(DbContextOptions<DataContext> options) : base(options)
        {
        }

        public DataContext(string connectionString)
        {
            _connectionString = connectionString;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);

            if (!optionsBuilder.IsConfigured && !string.IsNullOrEmpty(_connectionString))
            {
                optionsBuilder.UseSqlServer(_connectionString);
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DeviceSyncSchedule>(entity =>
            {
                entity.ToTable("DeviceSyncSchedule");
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<Device>()
                .HasOne(d => d.DeviceSyncSchedule)
                .WithMany(s => s.Devices)
                .HasForeignKey(d => d.DeviceSyncScheduleId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<DeviceSyncHistory>(entity =>
            {
                entity.ToTable("DeviceSyncHistory");
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<Device>(entity =>
            {
                entity.ToTable("Devices");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.IP).HasColumnName("IP");
                entity.Property(e => e.PORT).HasColumnName("PORT");
            });

            modelBuilder.Entity<Device>()
             .HasOne(d => d.MeterType)
             .WithMany(mt => mt.Devices)
             .HasForeignKey(d => d.MeterTypeId);

            modelBuilder.Entity<Profile>(entity =>
            {
                entity.ToTable("Profiles");
                entity.HasKey(e => e.ProfileId);
            });

            modelBuilder.Entity<Parameter>(entity =>
            {
                entity.ToTable("Parameters");
                entity.HasKey(e => e.Id);

                entity.HasOne(d => d.Profile)
                    .WithMany(p => p.Parameters)
                    .HasForeignKey(d => d.ProfileId);

                entity.HasOne(d => d.MeterType)
                    .WithMany(m => m.Parameters)
                    .HasForeignKey(d => d.MeterTypeId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<ReadingSession>(entity =>
            {
                entity.ToTable("ReadingSessions");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.EntryTimestampUtc).HasColumnName("EntryTimestampUtc");

                entity.HasOne(d => d.Device)
                    .WithMany()
                    .HasForeignKey(d => d.DeviceId);

                entity.HasOne(d => d.Profile)
                    .WithMany()
                    .HasForeignKey(d => d.ProfileId);

                // Add unique filtered index (prevent duplicates while allowing nulls)
                entity.HasIndex(e => new { e.DeviceId, e.ProfileId, e.EntryTimestampUtc })
                    .HasDatabaseName("IX_ReadingSessions_Device_Profile_Timestamp")
                    .IsUnique()
                    .HasFilter("[EntryTimestampUtc] IS NOT NULL");
            });

            modelBuilder.Entity<ReadingValue>(entity =>
            {
                entity.ToTable("ReadingValues");
                entity.HasKey(e => e.Id);

                entity.HasOne(d => d.Session)
                    .WithMany(s => s.Values)
                    .HasForeignKey(d => d.SessionId);

                entity.HasOne(d => d.Parameter)
                    .WithMany(p => p.ReadingValues)
                    .HasForeignKey(d => d.ParameterId);
            });

            modelBuilder.Entity<DeviceLatestReading>(entity =>
            {
                entity.ToTable("DeviceLatestReadings");
                entity.HasKey(e => new { e.DeviceId, e.ParameterId });

                entity.HasOne(d => d.Device)
                    .WithMany()
                    .HasForeignKey(d => d.DeviceId);

                entity.HasOne(d => d.Parameter)
                    .WithMany()
                    .HasForeignKey(d => d.ParameterId);
            });

            modelBuilder.Entity<DeviceProfileSyncState>(entity =>
            {
                entity.ToTable("DeviceProfileSyncState");
                entity.HasKey(e => new { e.DeviceId, e.ProfileId });

                entity.HasOne(d => d.Device)
                    .WithMany()
                    .HasForeignKey(d => d.DeviceId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.Profile)
                    .WithMany()
                    .HasForeignKey(d => d.ProfileId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<DeviceEvent>(entity =>
            {
                entity.ToTable("DeviceEvent");
                entity.HasKey(e => e.Id);

                entity.HasOne(d => d.Device)
                    .WithMany()
                    .HasForeignKey(d => d.DeviceId);

                entity.HasOne(d => d.Parameter)
                    .WithMany()
                    .HasForeignKey(d => d.ParameterId);
            });



        }

    }
}
