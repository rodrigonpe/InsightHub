ALTER TABLE business_hour_exceptions
DROP CONSTRAINT IF EXISTS uq_business_hour_exceptions_date;

CREATE UNIQUE INDEX IF NOT EXISTS uq_business_hour_exceptions_active_date
ON business_hour_exceptions (exception_date)
WHERE is_active = TRUE;