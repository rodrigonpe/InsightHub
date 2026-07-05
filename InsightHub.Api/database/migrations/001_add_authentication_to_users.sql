DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_type
        WHERE typname = 'user_role'
    ) THEN
        CREATE TYPE user_role AS ENUM (
            'ADMINISTRATOR',
            'SUPERVISOR',
            'SUPPORT'
        );
    END IF;
END $$;

ALTER TABLE users
ADD COLUMN IF NOT EXISTS password_hash TEXT NOT NULL DEFAULT '';

ALTER TABLE users
ADD COLUMN IF NOT EXISTS role user_role NOT NULL DEFAULT 'ADMINISTRATOR';