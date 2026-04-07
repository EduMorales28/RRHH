CREATE TABLE Periodos (
    Id INT IDENTITY PRIMARY KEY,
    Codigo NVARCHAR(20) NOT NULL UNIQUE,
    Estado INT NOT NULL DEFAULT 0,
    FechaCreacion DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    FechaCierre DATETIME2 NULL
);

CREATE TABLE Funcionarios (
    Id INT IDENTITY PRIMARY KEY,
    NumeroFuncionario NVARCHAR(50) NOT NULL UNIQUE,
    Nombre NVARCHAR(200) NOT NULL,
    Cedula NVARCHAR(50) NULL,
    Categoria NVARCHAR(100) NULL,
    TieneRetencion BIT NOT NULL DEFAULT 0,
    ResponsableRetencion NVARCHAR(200) NULL,
    BancoRetencion NVARCHAR(100) NULL,
    CuentaRetencion NVARCHAR(100) NULL,
    Activo BIT NOT NULL DEFAULT 1
);

CREATE TABLE CuentasPagoFuncionario (
    Id INT IDENTITY PRIMARY KEY,
    FuncionarioId INT NOT NULL,
    TipoPago NVARCHAR(100) NULL,
    Banco NVARCHAR(100) NULL,
    CuentaNueva NVARCHAR(100) NULL,
    CuentaVieja NVARCHAR(100) NULL,
    Activa BIT NOT NULL DEFAULT 1,
    CONSTRAINT FK_CuentasPagoFuncionario_Funcionarios FOREIGN KEY (FuncionarioId) REFERENCES Funcionarios(Id)
);

CREATE TABLE Obras (
    Id INT IDENTITY PRIMARY KEY,
    NumeroObra NVARCHAR(50) NOT NULL UNIQUE,
    Nombre NVARCHAR(200) NOT NULL,
    TipoObra INT NOT NULL,
    TipoObraOriginal NVARCHAR(100) NOT NULL,
    Cliente NVARCHAR(200) NULL,
    Activa BIT NOT NULL DEFAULT 1
);

CREATE TABLE HorasMensuales (
    Id INT IDENTITY PRIMARY KEY,
    PeriodoId INT NOT NULL,
    FuncionarioId INT NOT NULL,
    ObraId INT NOT NULL,
    RegistroOrigen INT NOT NULL,
    NombreFuncionarioExcel NVARCHAR(200) NULL,
    NombreObraExcel NVARCHAR(200) NULL,
    Categoria NVARCHAR(100) NULL,
    Cliente NVARCHAR(200) NULL,
    HorasComunes DECIMAL(18,2) NOT NULL,
    HorasExtras DECIMAL(18,2) NOT NULL,
    HorasEquivalentes DECIMAL(18,2) NOT NULL,
    CONSTRAINT FK_Horas_Periodos FOREIGN KEY (PeriodoId) REFERENCES Periodos(Id),
    CONSTRAINT FK_Horas_Funcionarios FOREIGN KEY (FuncionarioId) REFERENCES Funcionarios(Id),
    CONSTRAINT FK_Horas_Obras FOREIGN KEY (ObraId) REFERENCES Obras(Id)
);

CREATE TABLE PagosMensuales (
    Id INT IDENTITY PRIMARY KEY,
    PeriodoId INT NOT NULL,
    FuncionarioId INT NOT NULL,
    NombreFuncionarioExcel NVARCHAR(200) NULL,
    TipoObra INT NOT NULL,
    TipoObraOriginal NVARCHAR(100) NOT NULL,
    Cliente NVARCHAR(200) NULL,
    Adelanto DECIMAL(18,2) NOT NULL,
    Liquido DECIMAL(18,2) NOT NULL,
    Retencion DECIMAL(18,2) NOT NULL,
    TotalGenerado DECIMAL(18,2) NOT NULL,
    TipoPago NVARCHAR(100) NULL,
    Observacion NVARCHAR(300) NULL,
    CONSTRAINT FK_Pagos_Periodos FOREIGN KEY (PeriodoId) REFERENCES Periodos(Id),
    CONSTRAINT FK_Pagos_Funcionarios FOREIGN KEY (FuncionarioId) REFERENCES Funcionarios(Id)
);

CREATE TABLE CorridasProceso (
    Id INT IDENTITY PRIMARY KEY,
    PeriodoId INT NOT NULL,
    TipoProceso NVARCHAR(100) NOT NULL,
    CodigoCorrida NVARCHAR(50) NOT NULL,
    Usuario NVARCHAR(100) NOT NULL,
    FechaHora DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Corridas_Periodos FOREIGN KEY (PeriodoId) REFERENCES Periodos(Id)
);

CREATE TABLE DistribucionesCosto (
    Id INT IDENTITY PRIMARY KEY,
    PeriodoId INT NOT NULL,
    CorridaProcesoId INT NOT NULL,
    TipoObra INT NOT NULL,
    ObraId INT NOT NULL,
    Categoria NVARCHAR(100) NOT NULL,
    HorasLinea DECIMAL(18,2) NOT NULL,
    HorasTotalesTipoObra DECIMAL(18,2) NOT NULL,
    CostoTotalTipoObra DECIMAL(18,2) NOT NULL,
    PorcentajeParticipacion DECIMAL(18,8) NOT NULL,
    MontoLinea DECIMAL(18,2) NOT NULL,
    ValorHora DECIMAL(18,4) NOT NULL,
    Jornales DECIMAL(18,2) NOT NULL,
    CONSTRAINT FK_Distribucion_Periodos FOREIGN KEY (PeriodoId) REFERENCES Periodos(Id),
    CONSTRAINT FK_Distribucion_Corridas FOREIGN KEY (CorridaProcesoId) REFERENCES CorridasProceso(Id),
    CONSTRAINT FK_Distribucion_Obras FOREIGN KEY (ObraId) REFERENCES Obras(Id)
);

CREATE TABLE IncidenciasImportacion (
    Id INT IDENTITY PRIMARY KEY,
    FechaHora DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    TipoArchivo NVARCHAR(50) NOT NULL,
    PeriodoCodigo NVARCHAR(20) NULL,
    FilaOrigen INT NULL,
    CodigoReferencia NVARCHAR(100) NULL,
    Descripcion NVARCHAR(500) NOT NULL,
    Resuelta BIT NOT NULL DEFAULT 0,
    Resolucion NVARCHAR(500) NULL
);

CREATE TABLE AuditoriaEventos (
    Id INT IDENTITY PRIMARY KEY,
    FechaHora DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    Usuario NVARCHAR(100) NOT NULL,
    Modulo NVARCHAR(100) NOT NULL,
    Accion NVARCHAR(100) NOT NULL,
    Entidad NVARCHAR(100) NOT NULL,
    EntidadClave NVARCHAR(100) NOT NULL,
    Detalle NVARCHAR(500) NULL
);

CREATE INDEX IX_HorasMensuales_PeriodoId ON HorasMensuales(PeriodoId);
CREATE INDEX IX_PagosMensuales_PeriodoId ON PagosMensuales(PeriodoId);
CREATE INDEX IX_DistribucionesCosto_PeriodoId ON DistribucionesCosto(PeriodoId);
CREATE INDEX IX_AuditoriaEventos_FechaHora ON AuditoriaEventos(FechaHora DESC);
