-- Function to check for allocation overlaps
CREATE OR ALTER FUNCTION fn_CheckAllocationOverlap(
    @ResourceId UNIQUEIDENTIFIER,
    @PeriodStart DATETIMEOFFSET,
    @PeriodEnd DATETIMEOFFSET,
    @ExcludeAllocationId UNIQUEIDENTIFIER = NULL
)
RETURNS BIT
AS
BEGIN
    DECLARE @HasOverlap BIT = 0;
    
    IF EXISTS (
        SELECT 1
        FROM Allocations
        WHERE ResourceId = @ResourceId
          AND Id != ISNULL(@ExcludeAllocationId, '00000000-0000-0000-0000-000000000000')
          AND @PeriodStart < PeriodEnd
          AND PeriodStart < @PeriodEnd
    )
    BEGIN
        SET @HasOverlap = 1;
    END
    
    RETURN @HasOverlap;
END;
GO
