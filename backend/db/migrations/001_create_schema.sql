CREATE TABLE regions (
  id INT NOT NULL AUTO_INCREMENT,
  code VARCHAR(10) NOT NULL,
  name VARCHAR(60) NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_regions_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE skills (
  id INT NOT NULL AUTO_INCREMENT,
  code VARCHAR(20) NOT NULL,
  name VARCHAR(80) NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_skills_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE job_types (
  id INT NOT NULL AUTO_INCREMENT,
  code VARCHAR(20) NOT NULL,
  name VARCHAR(80) NOT NULL,
  default_minutes INT NOT NULL,
  required_skill_id INT NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_job_types_code (code),
  CONSTRAINT fk_job_types_skill FOREIGN KEY (required_skill_id) REFERENCES skills (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE users (
  id INT NOT NULL AUTO_INCREMENT,
  username VARCHAR(40) NOT NULL,
  display_name VARCHAR(80) NOT NULL,
  email VARCHAR(120) NOT NULL,
  password_hash VARCHAR(100) NOT NULL,
  role VARCHAR(20) NOT NULL,
  region_id INT NULL,
  active TINYINT(1) NOT NULL DEFAULT 1,
  created_at DATETIME NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_users_username (username),
  CONSTRAINT fk_users_region FOREIGN KEY (region_id) REFERENCES regions (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE customers (
  id INT NOT NULL AUTO_INCREMENT,
  account_ref VARCHAR(12) NOT NULL,
  name VARCHAR(120) NOT NULL,
  customer_type VARCHAR(20) NOT NULL,
  email VARCHAR(120) NULL,
  phone VARCHAR(15) NULL,
  address_line VARCHAR(200) NOT NULL,
  city VARCHAR(60) NOT NULL,
  postcode VARCHAR(10) NOT NULL,
  region_id INT NOT NULL,
  created_at DATETIME NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_customers_ref (account_ref),
  KEY ix_customers_region (region_id),
  CONSTRAINT fk_customers_region FOREIGN KEY (region_id) REFERENCES regions (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE customer_notes (
  id INT NOT NULL AUTO_INCREMENT,
  customer_id INT NOT NULL,
  author_user_id INT NOT NULL,
  note VARCHAR(1000) NOT NULL,
  created_at DATETIME NOT NULL,
  PRIMARY KEY (id),
  KEY ix_customer_notes_customer (customer_id),
  CONSTRAINT fk_cnotes_customer FOREIGN KEY (customer_id) REFERENCES customers (id),
  CONSTRAINT fk_cnotes_user FOREIGN KEY (author_user_id) REFERENCES users (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE sites (
  id INT NOT NULL AUTO_INCREMENT,
  customer_id INT NOT NULL,
  name VARCHAR(100) NOT NULL,
  address_line VARCHAR(200) NOT NULL,
  city VARCHAR(60) NOT NULL,
  postcode VARCHAR(10) NOT NULL,
  region_id INT NOT NULL,
  access_notes VARCHAR(300) NULL,
  created_at DATETIME NOT NULL,
  PRIMARY KEY (id),
  KEY ix_sites_customer (customer_id),
  KEY ix_sites_region (region_id),
  CONSTRAINT fk_sites_customer FOREIGN KEY (customer_id) REFERENCES customers (id),
  CONSTRAINT fk_sites_region FOREIGN KEY (region_id) REFERENCES regions (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE assets (
  id INT NOT NULL AUTO_INCREMENT,
  site_id INT NOT NULL,
  asset_type VARCHAR(20) NOT NULL,
  make VARCHAR(40) NOT NULL,
  model VARCHAR(60) NOT NULL,
  serial_number VARCHAR(40) NOT NULL,
  install_date DATE NOT NULL,
  last_service_date DATE NULL,
  status VARCHAR(20) NOT NULL,
  PRIMARY KEY (id),
  KEY ix_assets_site (site_id),
  CONSTRAINT fk_assets_site FOREIGN KEY (site_id) REFERENCES sites (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE engineers (
  id INT NOT NULL AUTO_INCREMENT,
  employee_no VARCHAR(10) NOT NULL,
  full_name VARCHAR(80) NOT NULL,
  email VARCHAR(120) NOT NULL,
  phone VARCHAR(20) NULL,
  region_id INT NOT NULL,
  grade VARCHAR(20) NOT NULL,
  active TINYINT(1) NOT NULL DEFAULT 1,
  hired_on DATE NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_engineers_no (employee_no),
  KEY ix_engineers_region (region_id),
  CONSTRAINT fk_engineers_region FOREIGN KEY (region_id) REFERENCES regions (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE engineer_skills (
  engineer_id INT NOT NULL,
  skill_id INT NOT NULL,
  PRIMARY KEY (engineer_id, skill_id),
  CONSTRAINT fk_eskills_engineer FOREIGN KEY (engineer_id) REFERENCES engineers (id),
  CONSTRAINT fk_eskills_skill FOREIGN KEY (skill_id) REFERENCES skills (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE shifts (
  id INT NOT NULL AUTO_INCREMENT,
  engineer_id INT NOT NULL,
  shift_date DATE NOT NULL,
  start_time TIME NOT NULL,
  end_time TIME NOT NULL,
  break_minutes INT NOT NULL DEFAULT 30,
  shift_type VARCHAR(20) NOT NULL,
  PRIMARY KEY (id),
  KEY ix_shifts_engineer_date (engineer_id, shift_date),
  CONSTRAINT fk_shifts_engineer FOREIGN KEY (engineer_id) REFERENCES engineers (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE parts (
  id INT NOT NULL AUTO_INCREMENT,
  sku VARCHAR(20) NOT NULL,
  name VARCHAR(100) NOT NULL,
  unit_cost DECIMAL(8,2) NOT NULL,
  stock_qty INT NOT NULL,
  reorder_level INT NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_parts_sku (sku)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE jobs (
  id INT NOT NULL AUTO_INCREMENT,
  job_ref VARCHAR(15) NOT NULL,
  customer_id INT NOT NULL,
  site_id INT NOT NULL,
  asset_id INT NULL,
  job_type_id INT NOT NULL,
  engineer_id INT NULL,
  region_id INT NOT NULL,
  priority VARCHAR(2) NOT NULL,
  status VARCHAR(20) NOT NULL,
  scheduled_start DATETIME NULL,
  scheduled_end DATETIME NULL,
  actual_start DATETIME NULL,
  actual_end DATETIME NULL,
  sla_due_at DATETIME NULL,
  outcome VARCHAR(30) NULL,
  first_time_fix TINYINT(1) NULL,
  description VARCHAR(500) NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_jobs_ref (job_ref),
  KEY ix_jobs_engineer_start (engineer_id, scheduled_start),
  KEY ix_jobs_asset (asset_id),
  KEY ix_jobs_status (status),
  CONSTRAINT fk_jobs_customer FOREIGN KEY (customer_id) REFERENCES customers (id),
  CONSTRAINT fk_jobs_site FOREIGN KEY (site_id) REFERENCES sites (id),
  CONSTRAINT fk_jobs_asset FOREIGN KEY (asset_id) REFERENCES assets (id),
  CONSTRAINT fk_jobs_type FOREIGN KEY (job_type_id) REFERENCES job_types (id),
  CONSTRAINT fk_jobs_engineer FOREIGN KEY (engineer_id) REFERENCES engineers (id),
  CONSTRAINT fk_jobs_region FOREIGN KEY (region_id) REFERENCES regions (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE job_notes (
  id INT NOT NULL AUTO_INCREMENT,
  job_id INT NOT NULL,
  author VARCHAR(80) NOT NULL,
  note VARCHAR(1000) NOT NULL,
  created_at DATETIME NOT NULL,
  PRIMARY KEY (id),
  KEY ix_job_notes_job (job_id),
  CONSTRAINT fk_jnotes_job FOREIGN KEY (job_id) REFERENCES jobs (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE job_parts (
  id INT NOT NULL AUTO_INCREMENT,
  job_id INT NOT NULL,
  part_id INT NOT NULL,
  quantity INT NOT NULL,
  PRIMARY KEY (id),
  KEY ix_job_parts_job (job_id),
  CONSTRAINT fk_jparts_job FOREIGN KEY (job_id) REFERENCES jobs (id),
  CONSTRAINT fk_jparts_part FOREIGN KEY (part_id) REFERENCES parts (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE import_batches (
  id INT NOT NULL AUTO_INCREMENT,
  file_name VARCHAR(200) NOT NULL,
  imported_at DATETIME NOT NULL,
  total_rows INT NOT NULL,
  created_rows INT NOT NULL,
  rejected_rows INT NOT NULL,
  PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE job_requests (
  id INT NOT NULL AUTO_INCREMENT,
  batch_id INT NOT NULL,
  row_no INT NOT NULL,
  customer_ref VARCHAR(12) NULL,
  site_postcode VARCHAR(10) NULL,
  job_type_code VARCHAR(20) NULL,
  priority VARCHAR(2) NULL,
  requested_date DATE NULL,
  description VARCHAR(500) NULL,
  result VARCHAR(20) NOT NULL,
  rejection_reason VARCHAR(300) NULL,
  job_id INT NULL,
  PRIMARY KEY (id),
  KEY ix_job_requests_batch (batch_id),
  CONSTRAINT fk_jreq_batch FOREIGN KEY (batch_id) REFERENCES import_batches (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
