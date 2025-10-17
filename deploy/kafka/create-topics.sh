#!/bin/bash
# Creates every claim-bridge topic with its .retry and .dlq lanes. Idempotent.
set -euo pipefail

BOOTSTRAP="${KAFKA_BOOTSTRAP:-kafka:9092}"
KAFKA_TOPICS="${KAFKA_TOPICS_BIN:-/opt/kafka/bin/kafka-topics.sh}"
PARTITIONS="${PARTITIONS:-3}"

TOPICS=(
  claims.batch-uploaded
  claims.claim-received
  claims.claim-scrubbed
  claims.claim-ready
  claims.status-changed
  payers.claim-submitted
  payers.ack-received
  payers.status-updated
  payers.remittance-received
  remittance.payment-posted
  remittance.claim-denied
  remittance.reversal-posted
  providers.enrollment-changed
  providers.webhook-subscription-changed
  denial-predictor.model-trained
)

until "$KAFKA_TOPICS" --bootstrap-server "$BOOTSTRAP" --list >/dev/null 2>&1; do
  echo "waiting for kafka at $BOOTSTRAP"
  sleep 3
done

create() {
  "$KAFKA_TOPICS" --bootstrap-server "$BOOTSTRAP" --create --if-not-exists --topic "$1" \
    --partitions "$2" --replication-factor 1 --config retention.ms="$3" >/dev/null
  echo "topic $1"
}

for topic in "${TOPICS[@]}"; do
  create "$topic" "$PARTITIONS" 604800000
  create "$topic.retry" 1 604800000
  # dead letters are kept a month for manual replay
  create "$topic.dlq" 1 2592000000
done
create healthchecks 1 3600000
