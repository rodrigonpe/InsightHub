INSERT INTO business_hours (
    id,
    day_of_week,
    is_open,
    start_time,
    end_time,
    is_active,
    created_by_user_id
)
VALUES
(gen_random_uuid(), 1, TRUE, '08:00', '17:00', TRUE, 'bebc9d2b-b497-4a69-8823-2c525d65adbd'),
(gen_random_uuid(), 2, TRUE, '08:00', '17:00', TRUE, 'bebc9d2b-b497-4a69-8823-2c525d65adbd'),
(gen_random_uuid(), 3, TRUE, '08:00', '17:00', TRUE, 'bebc9d2b-b497-4a69-8823-2c525d65adbd'),
(gen_random_uuid(), 4, TRUE, '08:00', '17:00', TRUE, 'bebc9d2b-b497-4a69-8823-2c525d65adbd'),
(gen_random_uuid(), 5, TRUE, '08:00', '17:00', TRUE, 'bebc9d2b-b497-4a69-8823-2c525d65adbd'),
(gen_random_uuid(), 6, FALSE, NULL, NULL, TRUE, 'bebc9d2b-b497-4a69-8823-2c525d65adbd'),
(gen_random_uuid(), 0, FALSE, NULL, NULL, TRUE, 'bebc9d2b-b497-4a69-8823-2c525d65adbd');
