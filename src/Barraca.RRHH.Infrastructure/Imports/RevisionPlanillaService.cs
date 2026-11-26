using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Barraca.RRHH.Application.DTOs;
using Barraca.RRHH.Application.Interfaces;
using Barraca.RRHH.Domain.Entities;
using Barraca.RRHH.Domain.Enums;
using Barraca.RRHH.Infrastructure.Data;
using Barraca.RRHH.Infrastructure.Helpers;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Barraca.RRHH.Infrastructure.Imports;

// Revisión previa a la importación: detecta en la planilla lo que el importador
// corregiría o descartaría en silencio. Solo lee; no modifica la base.
public class RevisionPlanillaService : IRevisionPlanillaService
{
    private readonly BarracaDbContext _db;

    public RevisionPlanillaService(BarracaDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ProblemaPlanillaDto>> RevisarFuncionariosAsync(string filePath)
    {
        var problemas = new List<ProblemaPlanillaDto>();
        using var stream = ExcelImportService.AbrirArchivoExcelParaLectura(filePath);
        using var workbook = new XLWorkbook(stream);
        if (!workbook.TryGetWorksheet("FUNCIONARIOS", out var ws))
        {
            problemas.Add(ErrorArchivo("La planilla no tiene la hoja FUNCIONARIOS.", "Verifique que eligió la planilla de funcionarios."));
            return problemas;
        }

        var enBase = await CargarFuncionariosAsync();
        var filasPorNumero = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var numerosPorNombre = new Dictionary<string, List<(string Numero, int Fila)>>();
        var numerosEnArchivo = ws.RowsUsed().Skip(1)
            .Select(r => r.Cell(1).GetString().Trim())
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var numero = row.Cell(1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(numero))
                continue;

            var fila = row.RowNumber();
            var nombre = row.Cell(2).GetString().Trim();
            var etiqueta = Etiqueta(numero, nombre);

            if (filasPorNumero.TryGetValue(numero, out var filaPrevia))
                problemas.Add(Error(fila, etiqueta, $"El número {numero} está repetido (también en la fila {filaPrevia}).",
                    "Solo se guarda la última fila. Deje una sola fila por funcionario."));
            else
                filasPorNumero[numero] = fila;

            if (string.IsNullOrWhiteSpace(row.Cell(3).GetString()))
                problemas.Add(Aviso(fila, etiqueta, "No tiene categoría.", "Complete la categoría: se usa en la distribución por obra."));

            var tipoPago = row.Cell(4).GetString().Trim();
            var tieneCuenta = !string.IsNullOrWhiteSpace(row.Cell(6).GetString()) || !string.IsNullOrWhiteSpace(row.Cell(7).GetString());
            if (tipoPago.Contains("RED", StringComparison.OrdinalIgnoreCase) && !tieneCuenta)
                problemas.Add(Aviso(fila, etiqueta, "Cobra por RedPagos pero no tiene cuenta nueva ni cuenta vieja.",
                    "Complete la cuenta: el listado de RedPagos saldría sin cuenta."));

            // Una persona puede tener un número por categoría: se compara el nombre con la categoría entre paréntesis.
            var clave = NormalizarNombre(nombre, conCategoria: true);
            if (clave.Length > 0)
            {
                if (!numerosPorNombre.TryGetValue(clave, out var lista))
                    numerosPorNombre[clave] = lista = new List<(string, int)>();
                lista.Add((numero, fila));

                var otroEnBase = enBase.Values.FirstOrDefault(f =>
                    NormalizarNombre(f.Nombre, conCategoria: true) == clave && !string.Equals(f.NumeroFuncionario, numero, StringComparison.OrdinalIgnoreCase));
                if (otroEnBase is not null && !numerosEnArchivo.Contains(otroEnBase.NumeroFuncionario))
                    problemas.Add(Aviso(fila, etiqueta,
                        $"En el sistema ya existe {Etiqueta(otroEnBase.NumeroFuncionario, otroEnBase.Nombre)} con el mismo nombre y otro número.",
                        "Confirme cuál es el número correcto para no tener al funcionario duplicado."));
            }
        }

        foreach (var (_, lista) in numerosPorNombre.Where(x => x.Value.Select(v => v.Numero).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
        {
            var numeros = string.Join(", ", lista.Select(x => $"{x.Numero} (fila {x.Fila})"));
            problemas.Add(Aviso(lista[0].Fila, numeros, "El mismo nombre aparece con números distintos.",
                "Puede ser un error de tipeo. Deje un solo número por funcionario."));
        }

        return Ordenar(problemas);
    }

    public async Task<IReadOnlyList<ProblemaPlanillaDto>> RevisarHorasAsync(string filePath, string periodo)
    {
        var problemas = new List<ProblemaPlanillaDto>();
        var periodoCodigo = (periodo ?? string.Empty).Trim();
        using var stream = ExcelImportService.AbrirArchivoExcelParaLectura(filePath);
        using var workbook = new XLWorkbook(stream);
        if (!workbook.TryGetWorksheet("HORAS", out var ws))
        {
            problemas.Add(ErrorArchivo("La planilla no tiene la hoja HORAS.", "Verifique que eligió la planilla de horas."));
            return problemas;
        }

        var funcionarios = await CargarFuncionariosAsync();
        var obras = (await _db.Obras.AsNoTracking().ToListAsync())
            .GroupBy(x => x.NumeroObra.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var filasDatos = ExcelImportService.FilasDatosHoras(ws).ToList();
        AgregarFilasIgnoradas(ws, filasDatos, problemas, "horas");

        if (filasDatos.Count == 0)
        {
            problemas.Add(ErrorArchivo("La planilla no tiene filas de horas para importar.", "Verifique que sea la planilla correcta y que tenga datos."));
            return Ordenar(problemas);
        }

        var obrasNuevasAvisadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tiposAvisados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filasValidas = 0;

        foreach (var row in filasDatos)
        {
            var fila = row.RowNumber();
            var numero = row.Cell(2).GetString().Trim();
            var nombre = row.Cell(3).GetString().Trim();
            var etiqueta = Etiqueta(numero, nombre);

            if (FilaDeOtroPeriodo(row.Cell(4).GetString().Trim(), periodoCodigo, fila, etiqueta, problemas))
                continue;

            var numeroObra = row.Cell(5).GetString().Trim();
            if (string.IsNullOrWhiteSpace(numeroObra))
            {
                problemas.Add(Error(fila, etiqueta, "No tiene número de obra: la fila se ignora.", "Complete el N° de obra."));
                continue;
            }

            filasValidas++;
            RevisarFuncionario(numero, nombre, fila, etiqueta, funcionarios, problemas);

            var tipoOriginal = row.Cell(7).GetString().Trim();
            if (!TipoObraParser.EsReconocido(tipoOriginal))
                problemas.Add(Error(fila, etiqueta, $"Tipo de obra '{tipoOriginal}' no reconocido: se tomaría como N-A.",
                    "Use CONSTRUCCION, INDUSTRIA Y COMERCIO, ADMINISTRACION, METALURGICA o N-A."));

            var nombreObra = row.Cell(6).GetString().Trim();
            if (!obras.TryGetValue(numeroObra, out var obra))
            {
                if (obrasNuevasAvisadas.Add(numeroObra))
                    problemas.Add(Aviso(fila, $"Obra {numeroObra} - {nombreObra}", "La obra no existe en el sistema: se va a crear automáticamente.",
                        "Si es una obra nueva está bien; si no, revise el número. Conviene importar OBRAS antes."));
            }
            else if (TipoObraParser.EsReconocido(tipoOriginal) && TipoObraParser.Parse(tipoOriginal) != obra.TipoObra && tiposAvisados.Add(numeroObra))
            {
                problemas.Add(Aviso(fila, $"Obra {numeroObra} - {obra.Nombre}",
                    $"En OBRAS figura como {obra.TipoObraOriginal} y en HORAS como {tipoOriginal}.",
                    "Al importar se cambia el tipo de la obra al de HORAS. Confirme cuál es el correcto."));
            }

            var celda8 = row.Cell(8);
            var layoutConClienteEn8 = !celda8.IsEmpty() && !celda8.TryGetValue<decimal>(out _);
            var comunes = ExcelImportService.LeerDecimal(layoutConClienteEn8 ? row.Cell(9) : row.Cell(8));
            var extras = ExcelImportService.LeerDecimal(layoutConClienteEn8 ? row.Cell(10) : row.Cell(9));

            if (comunes < 0 || extras < 0)
                problemas.Add(Error(fila, etiqueta, "Tiene horas negativas.", "Corrija las horas."));
            else if (comunes + extras == 0)
                problemas.Add(Aviso(fila, etiqueta, "La fila no tiene horas.", "No se tiene en cuenta para la distribución."));
        }

        if (filasValidas == 0)
            problemas.Add(ErrorArchivo($"Ninguna fila corresponde al período {periodoCodigo}.", "Verifique la columna PERIODO o el período seleccionado."));

        return Ordenar(problemas);
    }

    public async Task<IReadOnlyList<ProblemaPlanillaDto>> RevisarPagosAsync(string filePath, string periodo)
    {
        var problemas = new List<ProblemaPlanillaDto>();
        var periodoCodigo = (periodo ?? string.Empty).Trim();
        using var stream = ExcelImportService.AbrirArchivoExcelParaLectura(filePath);
        using var workbook = new XLWorkbook(stream);
        if (!workbook.TryGetWorksheet("PAGOS", out var ws))
        {
            problemas.Add(ErrorArchivo("La planilla no tiene la hoja PAGOS.", "Verifique que eligió la planilla de pagos."));
            return problemas;
        }

        var funcionarios = await CargarFuncionariosAsync();
        var filasDatos = ExcelImportService.FilasDatosPagos(ws).ToList();
        AgregarFilasIgnoradas(ws, filasDatos, problemas, "pagos");

        if (filasDatos.Count == 0)
        {
            problemas.Add(ErrorArchivo("La planilla no tiene filas de pagos para importar.", "Verifique que sea la planilla correcta y que tenga datos."));
            return Ordenar(problemas);
        }

        var totalMontos = 0m;
        var filasValidas = 0;
        var filasSinMontos = new List<ProblemaPlanillaDto>();

        foreach (var row in filasDatos)
        {
            var fila = row.RowNumber();
            var numero = row.Cell(2).GetString().Trim();
            var nombre = row.Cell(3).GetString().Trim();
            var etiqueta = Etiqueta(numero, nombre);

            if (FilaDeOtroPeriodo(row.Cell(4).GetString().Trim(), periodoCodigo, fila, etiqueta, problemas))
                continue;

            filasValidas++;
            var funcionario = RevisarFuncionario(numero, nombre, fila, etiqueta, funcionarios, problemas);

            var tipoOriginal = row.Cell(5).GetString().Trim();
            var tipoReconocido = TipoObraParser.EsReconocido(tipoOriginal);
            if (!tipoReconocido)
                problemas.Add(Error(fila, etiqueta, $"Tipo de obra '{tipoOriginal}' no reconocido: se tomaría como N-A y se pagaría en efectivo.",
                    "Use CONSTRUCCION, INDUSTRIA Y COMERCIO, ADMINISTRACION, METALURGICA o N-A."));
            var tipo = TipoObraParser.Parse(tipoOriginal);

            var adelanto = ExcelImportService.LeerDecimal(row.Cell(7));
            var liquido = ExcelImportService.LeerDecimal(row.Cell(8));
            var retencion = ExcelImportService.LeerDecimal(row.Cell(9));
            var totalColumna = ExcelImportService.LeerDecimal(row.Cell(11));

            if (adelanto < 0 || liquido < 0 || retencion < 0)
            {
                problemas.Add(Error(fila, etiqueta, "Tiene montos negativos: la fila se ignora.", "Corrija ADELANTO, LIQUIDO o RETENCION."));
                continue;
            }

            var total = adelanto + liquido + retencion;
            totalMontos += total;

            if (total == 0)
                filasSinMontos.Add(Aviso(fila, etiqueta, "La fila no tiene montos (adelanto, líquido y retención en 0).", "Si el funcionario no cobra este mes, puede borrar la fila."));
            else if (totalColumna > 0 && totalColumna != total)
                problemas.Add(Aviso(fila, etiqueta, $"El TOTAL ({totalColumna:N2}) no coincide con adelanto + líquido + retención ({total:N2}).",
                    "Se usa la suma de adelanto + líquido + retención."));

            if (funcionario is null)
                continue;

            if (liquido > 0 && tipoReconocido && tipo != TipoObra.NA)
            {
                var cuentasActivas = funcionario.CuentasPago.Count(c => c.Activa);
                if (cuentasActivas == 0)
                    problemas.Add(Aviso(fila, etiqueta, "Cobra por RedPagos pero no tiene cuenta bancaria cargada.",
                        "Complete la cuenta en la planilla FUNCIONARIOS e impórtela: el listado de RedPagos saldría sin cuenta."));
                else if (cuentasActivas > 1)
                    problemas.Add(Aviso(fila, etiqueta, $"Tiene {cuentasActivas} cuentas bancarias activas.",
                        "El listado de RedPagos usa una sola. Verifique en el PDF que sea la correcta."));
            }

            if (retencion > 0 && string.IsNullOrWhiteSpace(funcionario.ResponsableRetencion))
                problemas.Add(Aviso(fila, etiqueta, "Tiene retención pero no tiene responsable de la retención.",
                    "Complete responsable, banco y cuenta de retención en FUNCIONARIOS."));
        }

        if (filasValidas == 0)
            problemas.Add(ErrorArchivo($"Ninguna fila corresponde al período {periodoCodigo}.", "Verifique la columna PERIODO o el período seleccionado."));
        else if (totalMontos == 0)
            problemas.Add(ErrorArchivo("Todas las filas tienen ADELANTO, LIQUIDO y RETENCION vacíos o en 0.",
                "Parece la plantilla sin completar. Elija la planilla con los montos."));
        else
            problemas.AddRange(filasSinMontos);

        return Ordenar(problemas);
    }

    private async Task<Dictionary<string, Funcionario>> CargarFuncionariosAsync()
    {
        var lista = await _db.Funcionarios.AsNoTracking().Include(x => x.CuentasPago).ToListAsync();
        return lista
            .GroupBy(x => x.NumeroFuncionario.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static Funcionario? RevisarFuncionario(
        string numero, string nombre, int fila, string etiqueta,
        Dictionary<string, Funcionario> funcionarios, List<ProblemaPlanillaDto> problemas)
    {
        var clave = NormalizarNombre(nombre);

        if (!funcionarios.TryGetValue(numero, out var funcionario))
        {
            // Primero el mismo nombre y categoría; si no, cualquier número de esa persona.
            var claveCompleta = NormalizarNombre(nombre, conCategoria: true);
            var parecidos = funcionarios.Values.Where(f => claveCompleta.Length > 0 && NormalizarNombre(f.Nombre, conCategoria: true) == claveCompleta).ToList();
            if (parecidos.Count == 0)
                parecidos = funcionarios.Values.Where(f => clave.Length > 0 && NormalizarNombre(f.Nombre) == clave).ToList();

            problemas.Add(Error(fila, etiqueta, $"El funcionario {numero} no existe en el sistema.",
                parecidos.Count > 0
                    ? $"¿Es {string.Join(" o ", parecidos.Select(f => Etiqueta(f.NumeroFuncionario, f.Nombre)))}? Corrija el número en la planilla."
                    : "Se va a crear un funcionario nuevo sin categoría ni cuenta. Si es nuevo, impórtelo antes en FUNCIONARIOS."));
            return null;
        }

        var claveSistema = NormalizarNombre(funcionario.Nombre);
        if (clave.Length > 0 && claveSistema.Length > 0
            && !clave.Contains(claveSistema, StringComparison.Ordinal)
            && !claveSistema.Contains(clave, StringComparison.Ordinal))
        {
            problemas.Add(Aviso(fila, etiqueta, $"El nombre no coincide con el del funcionario {numero} en el sistema ({funcionario.Nombre}).",
                "Verifique que el número sea el correcto."));
        }

        if (!funcionario.Activo)
            problemas.Add(Aviso(fila, etiqueta, "El funcionario figura como inactivo.", "Si sigue trabajando, márquelo ACTIVO = SI en FUNCIONARIOS."));

        return funcionario;
    }

    private static bool FilaDeOtroPeriodo(string periodoFila, string periodoCodigo, int fila, string etiqueta, List<ProblemaPlanillaDto> problemas)
    {
        if (string.IsNullOrWhiteSpace(periodoFila) || string.Equals(periodoFila, periodoCodigo, StringComparison.OrdinalIgnoreCase))
            return false;

        problemas.Add(Error(fila, etiqueta, $"El período de la fila ({periodoFila}) es distinto al seleccionado ({periodoCodigo}): la fila se ignora.",
            "Verifique el período seleccionado en el programa o la columna PERIODO."));
        return true;
    }

    // Filas con número de funcionario que el importador descarta (sin ID numérico ni valores).
    private static void AgregarFilasIgnoradas(IXLWorksheet ws, List<IXLRow> filasDatos, List<ProblemaPlanillaDto> problemas, string tipo)
    {
        var aceptadas = filasDatos.Select(r => r.RowNumber()).ToHashSet();
        foreach (var row in ws.RowsUsed())
        {
            var numero = row.Cell(2).GetString().Trim();
            if (aceptadas.Contains(row.RowNumber()) || !decimal.TryParse(numero, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                continue;

            problemas.Add(Aviso(row.RowNumber(), Etiqueta(numero, row.Cell(3).GetString().Trim()),
                $"La fila no tiene ID ni {tipo}: se ignora.", "Si corresponde, complete el ID (columna A) y los valores."));
        }
    }

    private static string NormalizarNombre(string? nombre, bool conCategoria = false)
    {
        // "Walter Garcia (Herrero)" -> "WALTER GARCIA" (o "WALTER GARCIA HERRERO" con categoría)
        var sinParentesis = conCategoria ? nombre ?? string.Empty : Regex.Replace(nombre ?? string.Empty, @"\(.*?\)", " ");
        var descompuesto = sinParentesis.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var ch in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Etiqueta(string numero, string nombre) =>
        string.IsNullOrWhiteSpace(nombre) ? numero : $"{numero} - {nombre}";

    private static ProblemaPlanillaDto Error(int? fila, string funcionario, string problema, string sugerencia) =>
        new() { Fila = fila, Severidad = ProblemaPlanillaDto.Error, Funcionario = funcionario, Problema = problema, Sugerencia = sugerencia };

    private static ProblemaPlanillaDto Aviso(int? fila, string funcionario, string problema, string sugerencia) =>
        new() { Fila = fila, Severidad = ProblemaPlanillaDto.Aviso, Funcionario = funcionario, Problema = problema, Sugerencia = sugerencia };

    private static ProblemaPlanillaDto ErrorArchivo(string problema, string sugerencia) =>
        Error(null, "(toda la planilla)", problema, sugerencia);

    private static IReadOnlyList<ProblemaPlanillaDto> Ordenar(List<ProblemaPlanillaDto> problemas) =>
        problemas
            .OrderByDescending(x => x.EsError)
            .ThenBy(x => x.Fila ?? 0)
            .ToList();
}
