"""initial schema: model registry, prediction log, training claims

Revision ID: 0001
Revises:
Create Date: 2025-10-10 10:00:00
"""
import sqlalchemy as sa
from alembic import op

revision = "0001"
down_revision = None
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "model_versions",
        sa.Column("id", sa.Integer, primary_key=True, autoincrement=True),
        sa.Column("version", sa.String(40), nullable=False, unique=True),
        sa.Column("trained_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("auc", sa.Float, nullable=False),
        sa.Column("brier", sa.Float, nullable=False),
        sa.Column("n_train", sa.Integer, nullable=False),
        sa.Column("n_holdout", sa.Integer, nullable=False),
        sa.Column("denial_rate", sa.Float, nullable=False),
        sa.Column("importance", sa.JSON, nullable=False),
        sa.Column("path", sa.String(300), nullable=False),
        sa.Column("active", sa.Boolean, nullable=False, server_default=sa.false()),
    )
    op.create_table(
        "prediction_log",
        sa.Column("id", sa.Integer, primary_key=True, autoincrement=True),
        sa.Column("claim_id", sa.String(36), nullable=True, index=True),
        sa.Column("org_id", sa.String(36), nullable=True),
        sa.Column("payer_id", sa.String(20), nullable=False),
        sa.Column("model_version", sa.String(40), nullable=False),
        sa.Column("probability", sa.Float, nullable=False),
        sa.Column("factors", sa.JSON, nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
    )
    op.create_table(
        "training_claims",
        sa.Column("id", sa.Integer, primary_key=True, autoincrement=True),
        sa.Column("claim_number", sa.String(20), nullable=False, unique=True),
        sa.Column("features", sa.JSON, nullable=False),
        sa.Column("denied", sa.Boolean, nullable=False),
        sa.Column("carc", sa.String(5), nullable=True),
        sa.Column("adjudicated_at", sa.DateTime(timezone=True), nullable=True),
    )


def downgrade() -> None:
    op.drop_table("training_claims")
    op.drop_table("prediction_log")
    op.drop_table("model_versions")
