INSERT INTO users (
    id,
    name,
    email
)
VALUES (
    gen_random_uuid(),
    'Administrador',
    'rodrigo.nunes@insighthub.local'
);

SELECT * FROM users;

INSERT INTO holidays (
    id,
    name,
    description,
    month,
    day,
    is_recurring,
    scope,
    is_active,
    created_by_user_id
)
VALUES
(
    gen_random_uuid(),
    'Confraternização Universal',
    'Ano Novo',
    1,
    1,
    TRUE,
    'NATIONAL',
    TRUE,
    'bebc9d2b-b497-4a69-8823-2c525d65adbd'
),
(
    gen_random_uuid(),
    'Tiradentes',
    'Feriado nacional',
    4,
    21,
    TRUE,
    'NATIONAL',
    TRUE,
    'bebc9d2b-b497-4a69-8823-2c525d65adbd'
),
(
    gen_random_uuid(),
    'Dia do Trabalho',
    'Feriado nacional',
    5,
    1,
    TRUE,
    'NATIONAL',
    TRUE,
    'bebc9d2b-b497-4a69-8823-2c525d65adbd'
),
(
    gen_random_uuid(),
    'Independência do Brasil',
    'Feriado nacional',
    9,
    7,
    TRUE,
    'NATIONAL',
    TRUE,
    'bebc9d2b-b497-4a69-8823-2c525d65adbd'
),
(
    gen_random_uuid(),
    'Nossa Senhora Aparecida',
    'Padroeira do Brasil',
    10,
    12,
    TRUE,
    'NATIONAL',
    TRUE,
    'bebc9d2b-b497-4a69-8823-2c525d65adbd'
),
(
    gen_random_uuid(),
    'Finados',
    'Feriado nacional',
    11,
    2,
    TRUE,
    'NATIONAL',
    TRUE,
    'bebc9d2b-b497-4a69-8823-2c525d65adbd'
),
(
    gen_random_uuid(),
    'Proclamação da República',
    'Feriado nacional',
    11,
    15,
    TRUE,
    'NATIONAL',
    TRUE,
    'bebc9d2b-b497-4a69-8823-2c525d65adbd'
),
(
    gen_random_uuid(),
    'Natal',
    'Feriado nacional',
    12,
    25,
    TRUE,
    'NATIONAL',
    TRUE,
    'bebc9d2b-b497-4a69-8823-2c525d65adbd'
),
(
    gen_random_uuid(),
    'Consciência Negra',
    'Feriado nacional',
    11,
    20,
    TRUE,
    'NATIONAL',
    TRUE,
    'bebc9d2b-b497-4a69-8823-2c525d65adbd'
);

SELECT * FROM holidays;

