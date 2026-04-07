# Mapeo de plantillas reales v2

## FUNCIONARIOS
Hoja: `FUNCIONARIOS`
- A NUMERO -> NumeroFuncionario
- B NOMBRE -> Nombre
- C CATEGORIA -> Categoria
- D TIPO DE PAGO -> TipoPago de cuenta
- E BANCO -> Banco
- F CUENTA NUEVA -> CuentaNueva
- G CUENTA VIEJA -> CuentaVieja
- H RESPONSABLE RETENCION -> ResponsableRetencion
- I BANCO RETENCION -> BancoRetencion
- J CUENTA RETENCION -> CuentaRetencion
- K ACTIVO -> Activo

## OBRAS
Hoja: `OBRAS`
- A NUMERO -> NumeroObra
- B OBRA -> Nombre
- C TIPO_DE_OBRA -> TipoObraOriginal + TipoObra normalizado
- D CLIENTE -> Cliente
- E ACTIVA -> Activa

## HORAS
Hoja: `HORAS`
Datos desde fila 4
- A ID -> RegistroOrigen
- B NUMBER -> NumeroFuncionario
- C NAME -> NombreFuncionarioExcel
- D PERIODO -> Periodo
- E N° OBRA -> NumeroObra
- F OBRA -> NombreObraExcel
- G TIPO DE OBRA -> TipoObra original de obra si falta en maestro
- H HORAS COMUNES -> HorasComunes
- I HORAS EXTRAS -> HorasExtras
- J HORAS TOTALES -> validación referencial, se recalcula internamente como equivalentes
- K CLIENTE -> Cliente

## PAGOS
Hoja: `PAGOS`
Datos desde fila 4
- A NUMBER -> NumeroFuncionario
- B NAME -> NombreFuncionarioExcel
- C PERIODO -> Periodo
- D TIPO_DE_OBRA -> TipoObra
- E CLIENTE -> Cliente
- F ADELANTO -> Adelanto
- G LIQUIDO -> Liquido
- H RETENCION -> Retencion
- I TIPO_DE_PAGO -> TipoPago
- J OBSERVACION -> Observacion / total origen
