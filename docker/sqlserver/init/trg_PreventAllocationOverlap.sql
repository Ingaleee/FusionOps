-- Trigger to prevent allocation overlaps
CREATE OR ALTER TRIGGER trg_PreventAllocationOverlap
ON Allocations
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    
    IF EXISTS (
        SELECT 1
        FROM inserted i
        CROSS APPLY (
            SELECT fn_CheckAllocationOverlap(
                i.ResourceId,
                i.PeriodStart,
                i.PeriodEnd,
                i.Id
            ) AS HasOverlap
        ) check_overlap
        WHERE check_overlap.HasOverlap = 1
    )
    BEGIN
        RAISERROR('Allocation overlap detected: Resource already allocated for this time period', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END
END;
GO
