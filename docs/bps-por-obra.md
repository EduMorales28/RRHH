# BPS por obra

Pestaña **BPS por obra** de Sueldos Almirtaun, agregada el 26/11/2026. Reparte el BPS del mes entre las obras para cargarlo como gasto directo en SP. Reemplaza la planilla «Distribución BPS por Obras – C y IC».

## Cómo se reparte

1. **Facturas.** Se carga el total de cada factura del BPS de Construcción y de Industria y Comercio, tal cual se paga: nómina, fondos, IRPF y, en IyC, el Fondo Social Metalúrgico.
2. **Sede.** Son los administrativos de IyC: quienes solo tienen horas de IyC en la obra cuyo nombre contiene «SEDE» y tienen categoría «Administrativo». El programa pone 1 hora en esa obra a quien cobra en IyC sin horas.
   - Su BPS es el nominal de la planilla de ábaco de cada uno × la tasa de la Sede.
   - La tasa es la suma, concepto por concepto de la factura de IyC, de descuento ÷ monto gravado. En 2025 era 39,656 %.
   - Ese importe se resta del BPS de IyC.
   - **Quien cobra en IyC sin horas en obras y no es administrativo** (oficiales, peones, maquinistas, sin categoría) no va a la Sede: su BPS queda en el reparto de IyC por horas. Lo decidió el usuario el 26/11/2026; en octubre de 2026 fueron 8 personas. La pestaña los lista en un aviso.
   - La hora de referencia de la obra SEDE no se reparte, sea o no administrativo.
3. **Metalúrgicos.** Tienen 1 hora de referencia en su obra (9168 METALURGICA), no horas reales.
   - Por decisión del usuario del 26/11/2026, su BPS va como el de la Sede: nominal × la misma tasa.
   - El 27/11/2026 aclaró que se carga en la Sede: va en la factura `MMAAAASEDE` al depósito GASTOS2, no a la obra 9168.
   - Se resta del BPS de IyC, y esa hora no se reparte.
   - No vienen en ábaco: su nominal se escribe a mano en Pagos del mes, en la columna Nominal.
   - Si alguien tiene horas en Metalúrgica y también en obras de IyC, todas sus horas van al reparto de IyC.
4. **Construcción.** El total de Construcción se reparte entre las obras por sus horas equivalentes de Construcción: comunes + 2 × extras + lluvia.
5. **Industria y Comercio.** Lo que queda de IyC, después de la Sede y los metalúrgicos, se reparte entre las obras por sus horas equivalentes de IyC.
6. **No entran:** las horas N-A ni las de Administración.

Los importes van al centésimo, y lo que falta por redondeo va a los restos mayores: cada tipo suma exactamente lo facturado. Si falta un dato (una factura sin importe, la tasa o el nominal de alguien de la Sede), no se reparte nada y se muestra qué falta.

## Planilla para SP

El Excel `BPS_por_obra_AAAA-MM.xlsx` sale igual que como se cargó el BPS en SP hasta octubre de 2025.

- **Comprobante:** proveedor BPS, factura a crédito sin IVA.
- **Una por obra, con Construcción e IyC sumados:**
  - Número MMAAAA + obra, por ejemplo `102026147`.
  - Fecha del último día del mes.
  - Un renglón con el insumo BPS, cantidad 1.
  - Imputada a la obra sin remito.
- **Sede:** su BPS de administración, el de los metalúrgicos y el reparto de las obras SEDE van en una factura `MMAAAASEDE` al depósito GASTOS2.
- **N° en SP:** si en SP la obra tiene otro número, se carga en la columna «N° en SP» de la pestaña Obras.
  - La factura va a ese número de SP y el número de factura va sin ceros adelante, como `10202539` para la obra 039.
  - Si dos obras van al mismo número de SP, cada una lleva su factura con letra (`10202579A`, `10202579B`) y su nombre en la observación, como en SP en 08/2025.
  - Desde el 27/11/2026, la 9201 ESCUELA FLORES va en SP a la 079.

El programa no escribe en SP: la planilla se carga a mano.

## Validación

- **Octubre de 2025, con los datos de la planilla:**
  - El reparto da igual al centésimo que las facturas BPS cargadas en SP: 144, 139, 147, 149, 156 y 152 (con C e IC sumados).
  - La Sede da 206.058,31, igual que la planilla. En SP se cargó 206.058,51.
- **Octubre de 2026, sobre una copia de la base:** lo repartido suma exactamente lo facturado en cada tipo.
- **Los dos metalúrgicos de octubre de 2026:** con nominal 65.608 y 47.629,92, su BPS da 44.905,63 y se carga en la Sede.
- **Octubre de 2026 con los datos reales cargados el 26/11:**
  - Facturas de cargo 10/2026: Construcción 536.416 e IyC 522.735.
  - Tasa 39,656 %.
  - Sede de 15 administrativos: 293.610,64.
  - Metalúrgicos: 44.905,63, en la Sede.
  - Obras de IyC por horas: 184.218,73.
  - 16 facturas para SP por 1.059.151. La de la Sede es de 338.565,00.

## Datos nuevos en la base

- `PagosMensuales.Nominal`: el nominal de cada pago. Lo completa «Cargar planilla ábaco», y se puede escribir a mano en Pagos del mes (por ejemplo, el de los metalúrgicos). Los meses cargados antes del 26/11/2026 lo tienen en 0: hay que volver a cargar la planilla de ábaco de IyC y guardar.
- `FacturasBps`: las facturas BPS de cada mes.
- `BpsPeriodos`: la tasa de la Sede de cada mes. Si un mes no tiene tasa propia, se sugiere la del último mes anterior que la tenga.
- `Obras.CodigoSp`: el número de la obra en SP cuando es otro (vacío = el mismo).

`EsquemaBase.ActualizarAsync` las agrega a las bases existentes sin borrar datos.

## Pendiente de confirmar

- **Números de obra:** si una obra del programa no existe con ese número en SP, ponerle el «N° en SP» en la pestaña Obras antes de generar la planilla.
- **Meses ya cargados en SP:** en SP hay totales de BPS al depósito GASTOS2 (por ejemplo, $524.976 el 25/05/2026). Para no duplicar el gasto, no se carga por obra un mes que ya está en SP como total.
