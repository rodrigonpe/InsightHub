public class CreateHolidayRequest
{
    public required string Name { get; set; }
    public DateOnly HolidayDate { get; set; }
    public required string Scope { get; set; }
    public string? State { get; set; }
    public string? City { get; set; }
    public string? Description { get; set; }
}