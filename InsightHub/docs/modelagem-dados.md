# InsightHub

## Visão Geral

O InsightHub é uma plataforma de inteligência para operações de atendimento, capaz de integrar múltiplas fontes de dados (Movidesk, Zendesk, Zenvia, entre outras) e fornecer automações, dashboards e regras de negócio centralizadas.

A primeira funcionalidade desenvolvida é o módulo de calendário corporativo, responsável por gerenciar feriados nacionais, estaduais e municipais.

---

# Modelagem de Dados

## Entidade User

Representa um usuário do sistema responsável por criar e manter registros.

### Campos

| Campo | Tipo | Obrigatório | Descrição |
|---------|---------|---------|---------|
| Id | UUID | Sim | Identificador único |
| Name | VARCHAR(200) | Sim | Nome do usuário |
| Email | VARCHAR(255) | Sim | E-mail do usuário |
| IsActive | BOOLEAN | Sim | Indica se o usuário está ativo |
| CreatedAt | TIMESTAMP | Sim | Data de criação |
| UpdatedAt | TIMESTAMP | Sim | Data da última atualização |

---

## Entidade Holiday

Representa um feriado utilizado pelo motor de calendário do InsightHub.

### Campos

| Campo | Tipo | Obrigatório | Descrição |
|---------|---------|---------|---------|
| Id | UUID | Sim | Identificador único |
| Name | VARCHAR(200) | Sim | Nome do feriado |
| Description | TEXT | Não | Descrição do feriado |
| HolidayDate | DATE | Não | Data específica para feriados não recorrentes |
| Month | SMALLINT | Não | Mês para feriados recorrentes |
| Day | SMALLINT | Não | Dia para feriados recorrentes |
| IsRecurring | BOOLEAN | Sim | Indica se o feriado ocorre todos os anos |
| Scope | VARCHAR(20) | Sim | Escopo do feriado |
| State | VARCHAR(2) | Não | UF do estado |
| City | VARCHAR(100) | Não | Município |
| IsActive | BOOLEAN | Sim | Indica se o registro está ativo |
| CreatedByUserId | UUID | Sim | Usuário responsável pela criação |
| UpdatedByUserId | UUID | Não | Usuário responsável pela última atualização |
| CreatedAt | TIMESTAMP | Sim | Data de criação |
| UpdatedAt | TIMESTAMP | Sim | Data da última atualização |

---

## Escopos Permitidos

O campo Scope aceita apenas os seguintes valores:

| Valor |
|---------|
| NATIONAL |
| STATE |
| CITY |

---
## Entidade BusinessHours

Representa o horário padrão de atendimento por dia da semana.

### Campos

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| Id | UUID | Sim | Identificador único |
| DayOfWeek | SMALLINT | Sim | Dia da semana, sendo 0 domingo e 6 sábado |
| IsOpen | BOOLEAN | Sim | Indica se há atendimento no dia |
| StartTime | TIME | Não | Horário inicial de atendimento |
| EndTime | TIME | Não | Horário final de atendimento |
| IsActive | BOOLEAN | Sim | Indica se o registro está ativo |
| CreatedByUserId | UUID | Sim | Usuário responsável pela criação |
| UpdatedByUserId | UUID | Não | Usuário responsável pela última atualização |
| CreatedAt | TIMESTAMP | Sim | Data de criação |
| UpdatedAt | TIMESTAMP | Sim | Data da última atualização |

---

## Entidade BusinessHourException

Representa uma exceção de horário para uma data específica.

Utilizada para configurar atendimentos especiais, como expediente reduzido, recesso, feriados com atendimento ou datas sem atendimento.

### Campos

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| Id | UUID | Sim | Identificador único |
| ExceptionDate | DATE | Sim | Data da exceção |
| IsOpen | BOOLEAN | Sim | Indica se haverá atendimento nessa data |
| StartTime | TIME | Não | Horário inicial da exceção |
| EndTime | TIME | Não | Horário final da exceção |
| Reason | VARCHAR(255) | Não | Motivo resumido da exceção |
| Description | TEXT | Não | Descrição detalhada da exceção |
| IsActive | BOOLEAN | Sim | Indica se o registro está ativo |
| CreatedByUserId | UUID | Sim | Usuário responsável pela criação |
| UpdatedByUserId | UUID | Não | Usuário responsável pela última atualização |
| CreatedAt | TIMESTAMP | Sim | Data de criação |
| UpdatedAt | TIMESTAMP | Sim | Data da última atualização |

---

## Regras de Atendimento

O endpoint `/attendance/availability` deverá utilizar a seguinte ordem de decisão:

1. Verificar se existe exceção cadastrada para a data.
2. Se existir exceção ativa, ela terá prioridade sobre o horário padrão.
3. Se não existir exceção, verificar se a data é dia útil.
4. Se for dia útil, aplicar o horário padrão cadastrado em `BusinessHours`.
5. Se não houver horário padrão configurado, retornar atendimento indisponível com motivo `BUSINESS_HOURS_NOT_CONFIGURED`.

### Exemplo de horário padrão

```text
Segunda a sexta: 08:00 às 17:00
Sábado: fechado
Domingo: fechado
## Regras de Negócio

### Feriado Recorrente

Exemplo:

Natal

```text
Name = Natal
Month = 12
Day = 25
IsRecurring = true
```

### Feriado Não Recorrente

Exemplo:

Carnaval 2026

```text
Name = Carnaval
HolidayDate = 2026-02-17
IsRecurring = false
```

---

## Auditoria

Todos os registros de feriados devem possuir rastreabilidade.

Campos utilizados:

- CreatedByUserId
- UpdatedByUserId
- CreatedAt
- UpdatedAt

---

## Objetivo Futuro

O módulo de calendário deverá suportar:

- Cadastro via interface web
- Feriados nacionais
- Feriados estaduais
- Feriados municipais
- Integração com chatbot
- Integração com SLA
- Integração com automações de atendimento

## Requisito futuro - Calendário de expediente

O sistema deverá permitir configurar quais dias da semana são considerados úteis, para que clientes com expediente aos sábados ou escalas específicas possam ajustar o comportamento do endpoint /calendar/is-business-day.