CREATE TABLE integration_providers (
    id UUID PRIMARY KEY,
    code VARCHAR(30) NOT NULL UNIQUE,
    name VARCHAR(100) NOT NULL,
    base_url TEXT,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);
INSERT INTO integration_providers (id, code, name)
VALUES
(gen_random_uuid(),'MOVIDESK','Movidesk'),
(gen_random_uuid(),'ZENDESK','Zendesk'),
(gen_random_uuid(),'ZENVIA','Zenvia Costumer Cloud');

CREATE TABLE followup_rules (
    id UUID PRIMARY KEY,
    name VARCHAR(150) NOT NULL,
    provider_id UUID NOT NULL REFERENCES integration_providers(id),

    provider_status VARCHAR(100) NOT NULL,
    provider_reason VARCHAR(150),

    business_hours_to_wait INTEGER NOT NULL,

    template_name VARCHAR(150) NOT NULL,

    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE followup_tickets (
    id UUID PRIMARY KEY,

    provider_id UUID NOT NULL REFERENCES integration_providers(id),
    provider_ticket_id VARCHAR(100) NOT NULL,

    subject TEXT,

    provider_status VARCHAR(100),
    provider_reason VARCHAR(150),

    requester_name VARCHAR(200),

    owner_name VARCHAR(200),
    owner_team VARCHAR(200),

    opened_at TIMESTAMP,
    last_interaction_at TIMESTAMP,

    business_hours_elapsed NUMERIC(10,2) DEFAULT 0,

    next_followup_at TIMESTAMP,
    last_followup_sent_at TIMESTAMP,

    followup_status VARCHAR(30) NOT NULL DEFAULT 'MONITORING',

    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_followup_provider_ticket
        UNIQUE(provider_id, provider_ticket_id)
);

CREATE TABLE followup_ticket_events (
    id UUID PRIMARY KEY,

    followup_ticket_id UUID NOT NULL
        REFERENCES followup_tickets(id),

    event_type VARCHAR(50) NOT NULL,

    description TEXT,

    business_hours_elapsed NUMERIC(10,2),

    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);