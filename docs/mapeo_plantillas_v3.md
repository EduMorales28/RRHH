# Mapeo de plantillas reales v3

## FUNCIONARIOS
Hoja: `FUNCIONARIOS`
- Col 1: NUMERO
- Col 2: NOMBRE
- Col 3: CATEGORIA
- Col 4: TIPO DE PAGO
- Col 5: BANCO
- Col 6: CUENTA NUEVA
- Col 7: CUENTA VIEJA
- Col 8: RESPONSABLE RETENCION
- Col 9: BANCO RETENCION
- Col 10: CUENTA RETENCION
- Col 11: ACTIVO

## OBRAS
Hoja: `OBRAS`
- Col 1: NUMERO
- Col 2: OBRA
- Col 3: TIPO_DE_OBRA
- Col 4: CLIENTE
- Col 5: ACTIVA

## HORAS
Hoja: `HORAS`
- La fila útil de encabezado comienza en la fila 3.
- Los datos comienzan en la fila 4.
- Col 1: ID
- Col 2: NUMBER
- Col 3: NAME
- Col 4: PERIODO
- Col 5: N° OBRA
- Col 6: OBRA
- Col 7: TIPO DE OBRA
- Col 8: CLIENTE (layout actual) o HORAS COMUNES (layout legado)
- Col 9: HORAS COMUNES (layout actual) o HORAS EXTRAS (layout legado)
- Col 10: HORAS EXTRAS (layout actual) o HORAS TOTALES (layout legado)
- Col 11: HORAS TOTALES (layout actual) o CLIENTE (layout legado)

## PAGOS
Hoja: `PAGOS`
- La fila útil de encabezado comienza en la fila 3.
- Los datos comienzan en la fila 4.
- Col 1: ID
- Col 2: NUM FUNCIONARIO
- Col 3: NOMBRE FUNCIONARIO
- Col 4: PERIODO
- Col 5: TIPO_DE_OBRA
- Col 6: CLIENTE
- Col 7: ADELANTO
- Col 8: LIQUIDO
- Col 9: RETENCION
- Col 10: TIPO_DE_PAGO
- Col 11: TOTAL_DE_PAGO
- Col 12: OBSERVACION

## Observaciones de negocio
- Horas equivalentes = horas comunes + (horas extras × 2)
- Total generado usa columna K si viene informada; si no, se calcula como adelanto + líquido + retención
- Retenciones se pagan a terceros y se reportan aparte
- Si faltan funcionarios u obras durante importación, el sistema crea el registro y deja incidencia auditada
- Tipos de obra válidos: Construcción, Industria y Comercio, Administración y N-A
- Cliente forzado a `Almirtaun` para Construcción, Industria y Comercio y N-A
- Cliente variable para Administración
- Tipo de pago forzado: `Efectivo` solo para N-A y `RedPagos` para el resto
