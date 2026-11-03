
-- The first admin of the operator, so a fresh database can be signed into. No password here:
-- Shipment.DbMigrator finish sets the accounts' password from SHIPMENT_PASSWORD (.env, ADR 0011).
INSERT INTO identity.users (id, username, email, display_name, role)
VALUES ('019a2f00-0000-7000-8000-000000000001', 'k.lopuch', 'k.lopuch@ambitsys.eu', 'K. Lopuch', 'admin');
