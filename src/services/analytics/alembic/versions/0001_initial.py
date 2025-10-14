"""initial analytics schema: claim and scrub facts, payer health samples, model events

Revision ID: 0001
Revises:
Create Date: 2025-10-14 10:00:00
"""
import sqlalchemy as sa
from alembic import op

revision = "0001"
down_revision = None
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "claim_facts",
        sa.Column("claim_id", sa.String(36), primary_key=True),
        sa.Column("claim_number", sa.String(20), nullable=False, index=True),
        sa.Column("org_id", sa.String(36), nullable=False, index=True),
        sa.Column("payer_id", sa.String(20), nullable=False, index=True),
        sa.Column("total_charge", sa.Float, nullable=False),
        sa.Column("first_service_date", sa.Date, nullable=True),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("submitted_at", sa.DateTime(timezone=True), nullable=True, index=True),
        sa.Column("acked_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("accepted_first_pass", sa.Boolean, nullable=True),
        sa.Column("scrub_clean_first_pass", sa.Boolean, nullable=True),
        sa.Column("status", sa.String(20), nullable=False),
        sa.Column("adjudicated_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("paid_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("paid_amount", sa.Float, nullable=False),
        sa.Column("allowed_amount", sa.Float, nullable=False),
        sa.Column("patient_responsibility", sa.Float, nullable=False),
        sa.Column("adjustment_amount", sa.Float, nullable=False),
        sa.Column("denied", sa.Boolean, nullable=False),
        sa.Column("denial_category", sa.String(30), nullable=True),
        sa.Column("carc", sa.String(5), nullable=True),
        sa.Column("is_correction", sa.Boolean, nullable=False),
        sa.Column("reversed_amount", sa.Float, nullable=False),
        sa.Column("denial_risk", sa.Float, nullable=True),
    )
    op.create_table(
        "scrub_edit_facts",
        sa.Column("id", sa.Integer, primary_key=True, autoincrement=True),
        sa.Column("claim_id", sa.String(36), nullable=False, index=True),
        sa.Column("org_id", sa.String(36), nullable=False),
        sa.Column("payer_id", sa.String(20), nullable=False),
        sa.Column("rule_code", sa.String(20), nullable=False, index=True),
        sa.Column("severity", sa.String(10), nullable=False),
        sa.Column("first_attempt", sa.Boolean, nullable=False),
        sa.Column("at", sa.DateTime(timezone=True), nullable=False),
    )
    op.create_table(
        "payer_health_samples",
        sa.Column("id", sa.Integer, primary_key=True, autoincrement=True),
        sa.Column("at", sa.DateTime(timezone=True), nullable=False, index=True),
        sa.Column("payer_id", sa.String(20), nullable=False),
        sa.Column("circuit_state", sa.String(20), nullable=False),
        sa.Column("requests", sa.Integer, nullable=False),
        sa.Column("error_rate", sa.Float, nullable=False),
        sa.Column("p50_ms", sa.Float, nullable=True),
        sa.Column("p95_ms", sa.Float, nullable=True),
        sa.Column("queued", sa.Integer, nullable=False),
        sa.Column("deferred", sa.Integer, nullable=False),
    )
    op.create_table(
        "model_events",
        sa.Column("version", sa.String(40), primary_key=True),
        sa.Column("auc", sa.Float, nullable=False),
        sa.Column("brier", sa.Float, nullable=True),
        sa.Column("n_train", sa.Integer, nullable=False),
        sa.Column("trained_at", sa.DateTime(timezone=True), nullable=False),
    )
    op.create_table(
        "processed_events",
        sa.Column("event_id", sa.String(36), primary_key=True),
        sa.Column("processed_at", sa.DateTime(timezone=True), nullable=False),
    )


def downgrade() -> None:
    for table in ("processed_events", "model_events", "payer_health_samples", "scrub_edit_facts", "claim_facts"):
        op.drop_table(table)
