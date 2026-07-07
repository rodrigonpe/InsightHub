CREATE TYPE announcement_type AS ENUM (
    'INFO',
    'WARNING',
    'MAINTENANCE',
    'PAUSE',
    'CAMPAIGN'
);

CREATE TYPE announcement_status AS ENUM (
    'ACTIVE',
    'INACTIVE',
    'EXPIRED'
);

CREATE TYPE announcement_reason AS ENUM (
    'MAINTENANCE',
    'POWER_OUTAGE',
    'INSTABILITY',
    'HOLIDAY',
    'EMERGENCY',
    'OTHER'
);

CREATE TYPE announcement_action AS ENUM (
    'CREATED',
    'UPDATED',
    'ACTIVATED',
    'DEACTIVATED',
    'EXPIRED'
);

CREATE TABLE bot_announcements (
    id UUID PRIMARY KEY,

    title VARCHAR(150) NOT NULL,

    type announcement_type NOT NULL,
	 -- INFO
    -- WARNING
    -- MAINTENANCE
    -- PAUSE
    -- CAMPAIGN
    
    status announcement_status NOT NULL DEFAULT 'ACTIVE',
    -- ACTIVE
    -- INACTIVE
    -- EXPIRED
    
    reason announcement_reason,
    -- maintenance
    -- power_outage
    -- instability
    -- holiday
    -- emergency

    stop_bot BOOLEAN NOT NULL DEFAULT FALSE,

    message_html TEXT NOT NULL,

    message_text TEXT,

    starts_at TIMESTAMP,

    expires_at TIMESTAMP,

    created_by UUID NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),

    updated_by UUID,
    updated_at TIMESTAMP,

    deactivated_by UUID,
    deactivated_at TIMESTAMP,

    CONSTRAINT fk_bot_announcements_created_by
        FOREIGN KEY (created_by)
        REFERENCES users(id),

    CONSTRAINT fk_bot_announcements_updated_by
        FOREIGN KEY (updated_by)
        REFERENCES users(id),

    CONSTRAINT fk_bot_announcements_deactivated_by
        FOREIGN KEY (deactivated_by)
        REFERENCES users(id)
);