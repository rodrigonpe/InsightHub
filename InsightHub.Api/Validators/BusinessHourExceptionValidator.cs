namespace InsightHub.Api.Validators;

public static class BusinessHourExceptionValidator
{
    public static string? Validate(bool isOpen, TimeOnly? startTime, TimeOnly? endTime)
    {
        if (isOpen)
        {
            if (!startTime.HasValue || !endTime.HasValue)
                return "Informe o horário inicial e final quando o atendimento estiver aberto.";

            if (startTime >= endTime)
                return "O horário inicial deve ser menor que o horário final.";
        }

        return null;
    }
}