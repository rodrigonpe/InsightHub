namespace InsightHub.Api.Services.Movidesk.Dtos;

public class MovideskTicketDto
{
    public string? Id { get; set; }

    public string? Subject { get; set; }

    public string? Category { get; set; }

    public string? Urgency { get; set; }

    public string? Status { get; set; }

    public string? BaseStatus { get; set; }

    public string? Justification { get; set; }

    public DateTime? CreatedDate { get; set; }

    public DateTime? LastActionDate { get; set; }

    public DateTime? LastUpdate { get; set; }

    public MovideskPersonDto? CreatedBy { get; set; }

    public MovideskPersonDto? Owner { get; set; }

    public string? OwnerTeam { get; set; }

    public List<MovideskClientDto>? Clients { get; set; }

    public List<MovideskActionDto>? Actions { get; set; }
}

public class MovideskPersonDto
{
    public string? Id { get; set; }

    public int? PersonType { get; set; }

    public int? ProfileType { get; set; }

    public string? BusinessName { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }
}

public class MovideskClientDto : MovideskPersonDto
{
    public MovideskOrganizationDto? Organization { get; set; }
}

public class MovideskOrganizationDto
{
    public string? Id { get; set; }

    public string? BusinessName { get; set; }

    public string? Email { get; set; }
}

public class MovideskActionDto
{
    public int Id { get; set; }

    public int Type { get; set; }

    public string? Description { get; set; }

    public string? Status { get; set; }

    public string? Justification { get; set; }

    public DateTime? CreatedDate { get; set; }

    public MovideskPersonDto? CreatedBy { get; set; }
}