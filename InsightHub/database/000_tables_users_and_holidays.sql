/*First table*/
CREATE TABLE users (
    id UUID PRIMARY KEY,
    name VARCHAR(200) NOT NULL,
    email VARCHAR(255) NOT NULL UNIQUE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);
/*Second table*/
CREATE TABLE holidays (
    id UUID PRIMARY KEY,

    name VARCHAR(200) NOT NULL,

    description TEXT,

    holiday_date DATE,

    month SMALLINT,
    day SMALLINT,

    is_recurring BOOLEAN NOT NULL DEFAULT TRUE,

    scope VARCHAR(20) NOT NULL,

    state VARCHAR(2),
    city VARCHAR(100),

    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    created_by_user_id UUID NOT NULL,
    updated_by_user_id UUID,

    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT chk_holidays_scope
        CHECK (scope IN ('NATIONAL', 'STATE', 'CITY')),

    CONSTRAINT chk_holidays_month
        CHECK (
            month IS NULL
            OR (month BETWEEN 1 AND 12)
        ),

    CONSTRAINT chk_holidays_day
        CHECK (
            day IS NULL
            OR (day BETWEEN 1 AND 31)
        ),

    CONSTRAINT fk_holidays_created_by
        FOREIGN KEY (created_by_user_id)
        REFERENCES users(id),

    CONSTRAINT fk_holidays_updated_by
        FOREIGN KEY (updated_by_user_id)
        REFERENCES users(id)
);