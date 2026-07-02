/* Dias de trabalho padrão */
CREATE TABLE business_hours (
    id UUID PRIMARY KEY,

    day_of_week SMALLINT NOT NULL,
    is_open BOOLEAN NOT NULL DEFAULT TRUE,

    start_time TIME,
    end_time TIME,

    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    created_by_user_id UUID NOT NULL,
    updated_by_user_id UUID,

    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT chk_business_hours_day_of_week
        CHECK (day_of_week BETWEEN 0 AND 6),

    CONSTRAINT chk_business_hours_time_required_when_open
        CHECK (
            (is_open = FALSE AND start_time IS NULL AND end_time IS NULL)
            OR
            (is_open = TRUE AND start_time IS NOT NULL AND end_time IS NOT NULL AND start_time < end_time)
        ),

    CONSTRAINT fk_business_hours_created_by
        FOREIGN KEY (created_by_user_id)
        REFERENCES users(id),

    CONSTRAINT fk_business_hours_updated_by
        FOREIGN KEY (updated_by_user_id)
        REFERENCES users(id)
);
/* Exceções de horas de trabalho */
CREATE TABLE business_hour_exceptions (
    id UUID PRIMARY KEY,

    exception_date DATE NOT NULL,
    is_open BOOLEAN NOT NULL DEFAULT TRUE,

    start_time TIME,
    end_time TIME,

    reason VARCHAR(255),
    description TEXT,

    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    created_by_user_id UUID NOT NULL,
    updated_by_user_id UUID,

    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT uq_business_hour_exceptions_date
        UNIQUE (exception_date),

    CONSTRAINT chk_business_hour_exceptions_time_required_when_open
        CHECK (
            (is_open = FALSE AND start_time IS NULL AND end_time IS NULL)
            OR
            (is_open = TRUE AND start_time IS NOT NULL AND end_time IS NOT NULL AND start_time < end_time)
        ),

    CONSTRAINT fk_business_hour_exceptions_created_by
        FOREIGN KEY (created_by_user_id)
        REFERENCES users(id),

    CONSTRAINT fk_business_hour_exceptions_updated_by
        FOREIGN KEY (updated_by_user_id)
        REFERENCES users(id)
);