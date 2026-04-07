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
- Col 8: HORAS COMUNES
- Col 9: HORAS EXTRAS
- Col 10: HORAS TOTALES
- Col 11: CLIENTE

## PAGOS
Hoja: `PAGOS`
- La fila útil de encabezado comienza en la fila 3.
- Los datos comienzan en la fila 4.
- Col 1: NUMBER
- Col 2: NAME
- Col 3: PERIODO
- Col 4: TIPO_DE_OBRA
- Col 5: CLIENTE
- Col 6: ADELANTO
- Col 7: LIQUIDO
- Col 8: RETENCION
- Col 9: TIPO_DE_PAGO
- Col 10: OBSERVACION

## Observaciones de negocio
- Horas equivalentes = horas comunes + (horas extras × 2)
- Total generado = adelanto + líquido + retención
- Retenciones se pagan a terceros y se reportan aparte
- Si faltan funcionarios u obras durante importación, el sistema crea el registro y deja incidencia auditada
