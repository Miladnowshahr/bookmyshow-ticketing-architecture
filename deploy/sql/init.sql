-- Write-model schema (article §1.2.2). Seat-level writes are durable and
-- serialized here; everything ephemeral (locks, TTLs) lives in Redis
-- instead, so this table is never the bottleneck under load.

CREATE TABLE IF NOT EXISTS bookings (
    booking_id   UUID PRIMARY KEY,
    user_id      UUID NOT NULL,
    show_id      UUID NOT NULL,
    seat_ids     TEXT[] NOT NULL,
    status       VARCHAR(20) NOT NULL,
    amount       NUMERIC(10, 2) NOT NULL DEFAULT 0,
    created_at   TIMESTAMP NOT NULL DEFAULT now(),
    confirmed_at TIMESTAMP NULL,
    version      INT NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_bookings_show_id ON bookings (show_id);
CREATE INDEX IF NOT EXISTS idx_bookings_user_id ON bookings (user_id);
CREATE INDEX IF NOT EXISTS idx_bookings_status ON bookings (status);
