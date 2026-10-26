INSERT INTO regions (id, code, name) VALUES
(1, 'NW', 'North West'),
(2, 'NE', 'North East'),
(3, 'MID', 'Midlands'),
(4, 'LON', 'London'),
(5, 'SE', 'South East'),
(6, 'SW', 'South West');

INSERT INTO skills (id, code, name) VALUES
(1, 'GAS', 'Gas Safe - boilers and appliances'),
(2, 'MTR', 'Meter installation'),
(3, 'SMART', 'Smart meter commissioning'),
(4, 'ELEC', 'Electrical (18th edition)'),
(5, 'HW', 'Unvented hot water'),
(6, 'HP', 'Heat pump (MCS)'),
(7, 'OIL', 'Oil-fired appliances'),
(8, 'COMM', 'Commercial plant');

INSERT INTO job_types (id, code, name, default_minutes, required_skill_id) VALUES
(1, 'BOILER_SERVICE', 'Annual boiler service', 120, 1),
(2, 'BOILER_REPAIR', 'Boiler repair', 180, 1),
(3, 'BOILER_INSTALL', 'Boiler installation', 360, 1),
(4, 'SAFETY_CHECK', 'Landlord gas safety check', 75, 1),
(5, 'METER_INSTALL', 'Meter installation', 90, 2),
(6, 'METER_FAULT', 'Meter fault investigation', 120, 2),
(7, 'SMART_UPGRADE', 'Smart meter upgrade', 90, 3),
(8, 'HP_SURVEY', 'Heat pump survey', 150, 6),
(9, 'CYLINDER_FIT', 'Hot water cylinder fit', 240, 5);

INSERT INTO parts (id, sku, name, unit_cost, stock_qty, reorder_level) VALUES
(1, 'PCB-WB-GR', 'Printed circuit board, Worcester Greenstar', 118.5, 6, 3),
(2, 'DV-WB-01', 'Diverter valve, Worcester Bosch', 64.2, 1, 4),
(3, 'FAN-VA-ECO', 'Fan assembly, Vaillant ecoTEC', 96.0, 5, 3),
(4, 'PUMP-BX-8', 'Circulation pump, Baxi', 82.75, 7, 3),
(5, 'THERM-NTC', 'NTC thermistor', 14.9, 25, 10),
(6, 'IGN-ELEC', 'Ignition electrode', 18.4, 22, 8),
(7, 'HEX-ID-P', 'Plate heat exchanger, Ideal', 88.0, 4, 3),
(8, 'EXP-VES-8', 'Expansion vessel 8L', 39.95, 9, 4),
(9, 'PRV-15', 'Pressure relief valve 15mm', 12.3, 30, 10),
(10, 'GAS-VLV-G', 'Gas valve, universal', 72.1, 6, 4),
(11, 'MTR-E470', 'Smart electricity meter', 54.0, 40, 15),
(12, 'MTR-G4', 'Gas meter G4 diaphragm', 61.0, 28, 12),
(13, 'CT-CLAMP', 'CT clamp set', 21.5, 18, 8),
(14, 'FLUE-60', 'Flue extension 60/100', 16.75, 20, 8),
(15, 'CYL-210', 'Unvented cylinder 210L', 412.0, 3, 2),
(16, 'MAG-FILT', 'Magnetic filter 22mm', 58.6, 10, 5),
(17, 'HP-CTRL', 'Heat pump controller', 131.0, 2, 2),
(18, 'SEAL-KIT', 'Seal and gasket kit', 9.8, 50, 20);

INSERT INTO users (id, username, display_name, email, password_hash, role, region_id, active, created_at) VALUES
(1, 'admin', 'Dana Whitcombe', 'dana.whitcombe@corrin-utilities.example', '$2a$11$1C1LLZXTmyYtoXkN.1hRkO.v95.o7LNIqJ5.BeJ68c.MaXbltUEEF', 'admin', NULL, 1, '2024-01-08 09:00:00'),
(2, 'dmensah', 'Daniel Mensah', 'daniel.mensah@corrin-utilities.example', '$2a$11$hBUPt7kwaeHei9pgv1kFdFAOlzexAajUwo1DDZIWI7MKsugO88ZTR', 'manager', NULL, 1, '2024-01-08 09:00:00'),
(3, 'disp.nw', 'Helen Foy', 'helen.foy@corrin-utilities.example', '$2a$11$uLxR60a2VktTaaV5EiO3ZtS8yAAU.G/kiVA267xAVavWj8gevV0Vy', 'dispatcher', 1, 1, '2024-01-08 09:00:00'),
(4, 'disp.ne', 'Marcus Teague', 'marcus.teague@corrin-utilities.example', '$2a$11$/UIsYJdVnIbkBQlBkkzqOXWcVosU8huroL/Ng5EO39EbjozBrYQV.', 'dispatcher', 2, 1, '2024-01-08 09:00:00'),
(5, 'disp.mid', 'Anita Rao', 'anita.rao@corrin-utilities.example', '$2a$11$I4UFyCD7t2o6vQ1jEGQPEfda.hGxP47iAI3DBzjKHFYxniVnoL84V', 'dispatcher', 3, 1, '2024-01-08 09:00:00'),
(6, 'disp.lon', 'Tomasz Wrona', 'tomasz.wrona@corrin-utilities.example', '$2a$11$c8h88mZ6ZlT5eWiC.zBVMS0daqjDlcjSitepp2SD7bPcuNnK0a4f/', 'dispatcher', 4, 1, '2024-01-08 09:00:00'),
(7, 'disp.se', 'Gareth Pugh', 'gareth.pugh@corrin-utilities.example', '$2a$11$purV6wVAO5HwnLTWIu4gwjOvYYzU/ImSyAvCKPsdD.wED0x8VZ4DD', 'dispatcher', 5, 1, '2024-01-08 09:00:00'),
(8, 'disp.sw', 'Ffion Lewis', 'ffion.lewis@corrin-utilities.example', '$2a$11$QX2OTTocX9v1b.fX7sROIJl.amJRZiMMC/A95WH/HGxm9DNysnjtu', 'dispatcher', 6, 1, '2024-01-08 09:00:00'),
(9, 'mgr.nw', 'Rachel Okafor', 'rachel.okafor@corrin-utilities.example', '$2a$11$70s/7PBBZYDLaZTaJYwzhoB7ircRd/ME/774NBRqopJmyiABkL2Cc', 'manager', 1, 1, '2024-01-08 09:00:00'),
(10, 'mgr.south', 'Ian Bellamy', 'ian.bellamy@corrin-utilities.example', '$2a$11$XYfiNHMJs5l5y3EhuvCeq6QunNpU7dBvk.ok3Do5ZgpgyhpmF1zQU', 'manager', 5, 1, '2024-01-08 09:00:00'),
(11, 'planner', 'Sunita Desai', 'sunita.desai@corrin-utilities.example', '$2a$11$ydklAL7lUeROgoipUi0KzVsnwumofFM20tl2OLyguEwkS4Q1BhQg9', 'planner', NULL, 1, '2024-01-08 09:00:00'),
(12, 'parts', 'Colin Hargreaves', 'colin.hargreaves@corrin-utilities.example', '$2a$11$U50Hry4KnCxAnLjgFcjw0OxPh82St77IWcX.otC71UPYh9sJZJ2rD', 'stores', NULL, 1, '2024-01-08 09:00:00');

