using Barraca.RRHH.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Barraca.RRHH.Infrastructure.Data;

public class BarracaDbContext : DbContext
{
    public BarracaDbContext(DbContextOptions<BarracaDbContext> options) : base(options) { }

    public DbSet<Funcionario> Funcionarios => Set<Funcionario>();
    public DbSet<CuentaPagoFuncionario> CuentasPagoFuncionario => Set<CuentaPagoFuncionario>();
    public DbSet<Obra> Obras => Set<Obra>();
    public DbSet<Periodo> Periodos => Set<Periodo>();
    public DbSet<HoraMensual> HorasMensuales => Set<HoraMensual>();
    public DbSet<PagoMensual> PagosMensuales => Set<PagoMensual>();
    public DbSet<CorridaProceso> CorridasProceso => Set<CorridaProceso>();
    public DbSet<DistribucionCosto> DistribucionesCosto => Set<DistribucionCosto>();
    public DbSet<AuditoriaEvento> AuditoriaEventos => Set<AuditoriaEvento>();
    public DbSet<IncidenciaImportacion> IncidenciasImportacion => Set<IncidenciaImportacion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Funcionario>(entity =>
        {
            entity.HasIndex(x => x.NumeroFuncionario).IsUnique();
            entity.Property(x => x.NumeroFuncionario).HasMaxLength(50);
            entity.Property(x => x.Nombre).HasMaxLength(200);
            entity.Property(x => x.Categoria).HasMaxLength(100);
            entity.Property(x => x.Cedula).HasMaxLength(50);
            entity.Property(x => x.ResponsableRetencion).HasMaxLength(200);
            entity.Property(x => x.BancoRetencion).HasMaxLength(100);
            entity.Property(x => x.CuentaRetencion).HasMaxLength(100);
        });

        modelBuilder.Entity<CuentaPagoFuncionario>(entity =>
        {
            entity.Property(x => x.TipoPago).HasMaxLength(100);
            entity.Property(x => x.Banco).HasMaxLength(100);
            entity.Property(x => x.CuentaNueva).HasMaxLength(100);
            entity.Property(x => x.CuentaVieja).HasMaxLength(100);
            entity.HasOne(x => x.Funcionario)
                .WithMany(x => x.CuentasPago)
                .HasForeignKey(x => x.FuncionarioId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Obra>(entity =>
        {
            entity.HasIndex(x => x.NumeroObra).IsUnique();
            entity.Property(x => x.NumeroObra).HasMaxLength(50);
            entity.Property(x => x.Nombre).HasMaxLength(200);
            entity.Property(x => x.Cliente).HasMaxLength(200);
            entity.Property(x => x.TipoObraOriginal).HasMaxLength(100);
        });

        modelBuilder.Entity<Periodo>(entity =>
        {
            entity.HasIndex(x => x.Codigo).IsUnique();
            entity.Property(x => x.Codigo).HasMaxLength(20);
        });

        modelBuilder.Entity<HoraMensual>(entity =>
        {
            entity.Property(x => x.NombreFuncionarioExcel).HasMaxLength(200);
            entity.Property(x => x.NombreObraExcel).HasMaxLength(200);
            entity.Property(x => x.Categoria).HasMaxLength(100);
            entity.Property(x => x.Cliente).HasMaxLength(200);
            entity.HasOne(x => x.Periodo).WithMany().HasForeignKey(x => x.PeriodoId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Funcionario).WithMany().HasForeignKey(x => x.FuncionarioId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PagoMensual>(entity =>
        {
            entity.Property(x => x.NombreFuncionarioExcel).HasMaxLength(200);
            entity.Property(x => x.TipoObraOriginal).HasMaxLength(100);
            entity.Property(x => x.Cliente).HasMaxLength(200);
            entity.Property(x => x.TipoPago).HasMaxLength(100);
            entity.Property(x => x.Observacion).HasMaxLength(300);
            entity.HasOne(x => x.Periodo).WithMany().HasForeignKey(x => x.PeriodoId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Funcionario).WithMany().HasForeignKey(x => x.FuncionarioId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CorridaProceso>(entity =>
        {
            entity.Property(x => x.TipoProceso).HasMaxLength(100);
            entity.Property(x => x.CodigoCorrida).HasMaxLength(50);
            entity.Property(x => x.Usuario).HasMaxLength(100);
            entity.HasOne(x => x.Periodo).WithMany().HasForeignKey(x => x.PeriodoId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DistribucionCosto>(entity =>
        {
            entity.Property(x => x.Categoria).HasMaxLength(100);
            entity.HasOne(x => x.Periodo).WithMany().HasForeignKey(x => x.PeriodoId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.CorridaProceso).WithMany().HasForeignKey(x => x.CorridaProcesoId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditoriaEvento>(entity =>
        {
            entity.Property(x => x.Usuario).HasMaxLength(100);
            entity.Property(x => x.Modulo).HasMaxLength(100);
            entity.Property(x => x.Accion).HasMaxLength(100);
            entity.Property(x => x.Entidad).HasMaxLength(100);
            entity.Property(x => x.EntidadClave).HasMaxLength(100);
            entity.Property(x => x.Detalle).HasMaxLength(500);
        });

        modelBuilder.Entity<IncidenciaImportacion>(entity =>
        {
            entity.Property(x => x.TipoArchivo).HasMaxLength(50);
            entity.Property(x => x.PeriodoCodigo).HasMaxLength(20);
            entity.Property(x => x.CodigoReferencia).HasMaxLength(100);
            entity.Property(x => x.Descripcion).HasMaxLength(500);
            entity.Property(x => x.Resolucion).HasMaxLength(500);
        });
    }
}
