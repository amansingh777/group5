-- Run once as a MySQL admin (for example: mysql -u root -p < db-setup/01-create-users.sql)
CREATE DATABASE IF NOT EXISTS corrin CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE USER IF NOT EXISTS 'corrin_app'@'%' IDENTIFIED BY 'Corrin!App2026';
GRANT ALL PRIVILEGES ON corrin.* TO 'corrin_app'@'%';
FLUSH PRIVILEGES;
