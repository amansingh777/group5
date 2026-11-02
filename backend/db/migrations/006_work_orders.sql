CREATE TABLE visits (
  id INT NOT NULL AUTO_INCREMENT,
  job_id INT NOT NULL,
  engineer_id INT NULL,
  visit_no INT NOT NULL,
  scheduled_start DATETIME NULL,
  scheduled_end DATETIME NULL,
  actual_start DATETIME NULL,
  actual_end DATETIME NULL,
  outcome VARCHAR(20) NULL,
  created_at DATETIME NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_visits_job_visit_no (job_id, visit_no),
  KEY ix_visits_engineer_start (engineer_id, scheduled_start),
  CONSTRAINT ck_visits_outcome CHECK (outcome IS NULL OR outcome IN ('Fixed', 'PartsNeeded', 'NoAccess', 'FollowUpRequired')),
  CONSTRAINT fk_visits_job FOREIGN KEY (job_id) REFERENCES jobs (id),
  CONSTRAINT fk_visits_engineer FOREIGN KEY (engineer_id) REFERENCES engineers (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE reorder_requests (
  id INT NOT NULL AUTO_INCREMENT,
  part_id INT NOT NULL,
  status VARCHAR(10) NOT NULL,
  expected_on DATE NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NOT NULL,
  PRIMARY KEY (id),
  KEY ix_reorder_requests_part_status (part_id, status),
  CONSTRAINT ck_reorder_requests_status CHECK (status IN ('Open', 'Ordered', 'Received')),
  CONSTRAINT fk_reorder_requests_part FOREIGN KEY (part_id) REFERENCES parts (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
