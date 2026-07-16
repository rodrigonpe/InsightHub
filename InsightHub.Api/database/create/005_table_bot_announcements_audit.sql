CREATE TABLE bot_announcements_audit (
    id UUID PRIMARY KEY,

    announcement_id UUID NOT NULL,

    action announcement_action NOT NULL,
	 -- CREATED
    -- UPDATED
    -- ACTIVATED
    -- DEACTIVATED
    -- EXPIRED
    
    old_data JSONB,

    new_data JSONB,

    performed_by UUID NOT NULL,

    performed_at TIMESTAMP NOT NULL DEFAULT NOW(),

    ip_address VARCHAR(45),

    user_agent TEXT,

    CONSTRAINT fk_bot_audit_announcement
        FOREIGN KEY (announcement_id)
        REFERENCES bot_announcements(id),

    CONSTRAINT fk_bot_audit_user
        FOREIGN KEY (performed_by)
        REFERENCES users(id)
);