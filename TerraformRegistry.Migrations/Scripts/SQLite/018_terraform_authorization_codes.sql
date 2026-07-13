CREATE TABLE terraform_authorization_codes (
    code_hash TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    client_id TEXT NOT NULL,
    redirect_uri TEXT NOT NULL,
    state TEXT NOT NULL,
    code_challenge TEXT NOT NULL,
    code_challenge_method TEXT NOT NULL,
    expires_at TEXT NOT NULL
);

CREATE INDEX idx_terraform_authorization_codes_expires_at
    ON terraform_authorization_codes(expires_at);
