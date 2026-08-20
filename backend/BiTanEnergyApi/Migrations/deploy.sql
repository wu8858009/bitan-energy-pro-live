IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820102838_InitialCreate'
)
BEGIN
    CREATE TABLE [AdminUsers] (
        [Id] int NOT NULL IDENTITY,
        [Username] nvarchar(100) NOT NULL,
        [PasswordHash] nvarchar(500) NOT NULL,
        [Role] nvarchar(50) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AdminUsers] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820102838_InitialCreate'
)
BEGIN
    CREATE TABLE [Sites] (
        [Id] int NOT NULL IDENTITY,
        [Group] nvarchar(200) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Location] nvarchar(300) NOT NULL,
        [MeterNo] nvarchar(100) NOT NULL,
        [Type] nvarchar(20) NOT NULL,
        [BasePrev] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_Sites] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820102838_InitialCreate'
)
BEGIN
    CREATE TABLE [MonthlyReadings] (
        [Id] int NOT NULL IDENTITY,
        [SiteId] int NOT NULL,
        [MonthKey] nvarchar(7) NOT NULL,
        [CurrentValue] decimal(18,2) NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_MonthlyReadings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MonthlyReadings_Sites_SiteId] FOREIGN KEY ([SiteId]) REFERENCES [Sites] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820102838_InitialCreate'
)
BEGIN
    CREATE TABLE [ReadingPhotos] (
        [Id] int NOT NULL IDENTITY,
        [MonthlyReadingId] int NOT NULL,
        [FilePath] nvarchar(400) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [UploadedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ReadingPhotos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ReadingPhotos_MonthlyReadings_MonthlyReadingId] FOREIGN KEY ([MonthlyReadingId]) REFERENCES [MonthlyReadings] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820102838_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AdminUsers_Username] ON [AdminUsers] ([Username]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820102838_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MonthlyReadings_SiteId_MonthKey] ON [MonthlyReadings] ([SiteId], [MonthKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820102838_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ReadingPhotos_MonthlyReadingId] ON [ReadingPhotos] ([MonthlyReadingId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820102838_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260820102838_InitialCreate', N'8.0.11');
END;
GO

COMMIT;
GO

