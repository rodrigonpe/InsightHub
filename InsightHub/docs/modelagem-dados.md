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