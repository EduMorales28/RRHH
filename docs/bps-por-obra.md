# BPS por obra

Pestaña **BPS por obra** de Sueldos Almirtaun, agregada el 26/11/2026. Reparte el BPS del mes entre las obras para cargarlo como gasto directo en SP. Reemplaza la planilla «Distribución BPS por Obras – C y IC».

## Cómo se reparte

1. **Facturas.** Se carga el total de cada factura del BPS de Construcción y de Industria y Comercio, tal cual se paga: nómina, fondos, IRPF y, en IyC, el Fondo Social Metalúrgico.
2. **Sede.** Son los administrativos de IyC: quienes solo tienen horas de IyC en la obra cuyo nombre contiene «SEDE» (el programa les pone 1 hora ahí).
   - Su BPS es el nominal de la planilla de ábaco de cada uno × la tasa de la Sede.
   - La tasa es la suma, concepto por concepto de la factura de IyC, de descuento ÷ monto gravado. En 2025 era 39,656 %.
   - Ese importe se resta del BPS de IyC, y las horas de esas personas no se reparten.
3. **Construcción.** El total de Construcción se reparte entre las obras por sus horas equivalentes de Construcción: comunes + 2 × extras + lluvia.
4. **Industria y Comercio.** Lo que queda de IyC se reparte entre las obras por sus horas equivalentes de IyC. Metalúrgica va con IyC, porque los paga la empresa de IyC.
5. **No entran:** las horas N-A ni las de Administración.

Los importes van al centésimo, y lo que falta por redondeo va a los restos mayores: cada tipo suma exactamente lo facturado. Si falta un dato (una factura sin importe, la tasa o el nominal de alguien de la Sede), no se reparte nada y se muestra qué falta.

## Planilla para SP

El Excel `BPS_por_obra_AAAA-MM.xlsx` sale igual que como se cargó el BPS en SP hasta octubre de 2025.

- **Comprobante:** proveedor BPS, factura a crédito sin IVA.
- **Una por obra, con Construcción e IyC sumados:**
  - Número MMAAAA + obra, por ejemplo `102026147`.
  - Fecha del último día del mes.
  - Un renglón con el insumo BPS, cantidad 1.
  - Imputada a la obra sin remito.
- **Sede:** su BPS de administración más el reparto de las obras SEDE va en una factura `MMAAAASEDE` al depósito GASTOS2.

El programa no escribe en SP: la planilla se carga a mano.

## Validación

- **Octubre de 2025, con los datos de la planilla:**
  - El reparto da igual al centésimo que las facturas BPS cargadas en SP: 144, 139, 147, 149, 156 y 152 (con C e IC sumados).
  - La Sede da 206.058,31, igual que la planilla. En SP se cargó 206.058,51.
- **Octubre de 2026, sobre una copia de la base:** lo repartido suma exactamente lo facturado en cada tipo.

## Datos nuevos en la base

- `PagosMensuales.Nominal`: el nominal de ábaco. Lo completa «Cargar planilla ábaco» y se ve en Pagos del mes. Los meses cargados antes del 26/11/2026 lo tienen en 0: hay que volver a cargar la planilla de ábaco de IyC y guardar.
- `FacturasBps`: las facturas BPS de cada mes.
- `BpsPeriodos`: la tasa de la Sede de cada mes. Si un mes no tiene tasa propia, se sugiere la del último mes anterior que la tenga.

`EsquemaBase.ActualizarAsync` las agrega a las bases existentes sin borrar datos.

## Pendiente de confirmar

- **Metalúrgica:** sus horas son de referencia (1 h por persona), así que su BPS se reparte sobre todo entre las obras de IyC. Si conviene, se puede calcular como la Sede (nominal × tasa) e imputarlo a su obra.
- **Números de obra:** los del programa que no existen en SP, como 9168 o 9201, hay que corregirlos en la planilla antes de cargar.
- **Meses ya cargados en SP:** en SP hay totales de BPS al depósito GASTOS2 (por ejemplo, $524.976 el 25/05/2026). Para no duplicar el gasto, no se carga por obra un mes que ya está en SP como total.
