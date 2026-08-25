namespace Ten21.Domain.Enums;

/// <summary>US-29: which matrix column a batch-assign request targets -- a plain Guid?
/// alone can't distinguish "leave this column alone" from "clear it to none", so the request
/// names the column explicitly and treats its ValueId (null or not) as the new value.</summary>
public enum MatrixBatchField
{
    UnitGroup,
    UnitTier,
}
