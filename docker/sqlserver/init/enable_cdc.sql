USE master;
EXEC sys.sp_cdc_enable_db;
GO
USE FusionOpsWorkforce;
EXEC sys.sp_cdc_enable_table
    @source_schema = N'dbo',
    @source_name   = N'Allocations',
    @role_name     = NULL,
    @supports_net_changes = 0;
GO 