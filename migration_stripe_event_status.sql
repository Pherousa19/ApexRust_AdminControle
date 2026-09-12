-- Makes Stripe event claims recoverable after a failed webhook execution.
ALTER TABLE stripe_events ADD COLUMN status TEXT NOT NULL DEFAULT 'processing';
