{{- define "cb.fullname" -}}
{{- printf "%s-%s" .Release.Name .name | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "cb.labels" -}}
app.kubernetes.io/name: {{ .name }}
app.kubernetes.io/instance: {{ .root.Release.Name }}
app.kubernetes.io/part-of: claim-bridge
app.kubernetes.io/version: {{ .root.Values.global.imageTag | quote }}
helm.sh/chart: {{ .root.Chart.Name }}-{{ .root.Chart.Version }}
{{- end -}}

{{- define "cb.selector" -}}
app.kubernetes.io/name: {{ .name }}
app.kubernetes.io/instance: {{ .root.Release.Name }}
{{- end -}}

{{- define "cb.image" -}}
{{- if .root.Values.global.imageRegistry -}}
{{- printf "%s/claim-bridge/%s:%s" .root.Values.global.imageRegistry .svc.image .root.Values.global.imageTag -}}
{{- else -}}
{{- printf "claim-bridge/%s:%s" .svc.image .root.Values.global.imageTag -}}
{{- end -}}
{{- end -}}

{{- define "cb.secretName" -}}
{{- default (printf "%s-secrets" .Release.Name) .Values.global.existingSecret -}}
{{- end -}}

{{/* true when a service is switched on (services are on unless enabled: false) */}}
{{- define "cb.enabled" -}}
{{- if hasKey . "enabled" -}}{{ .enabled }}{{- else -}}true{{- end -}}
{{- end -}}

{{/* host name of an in-cluster dependency when localDependencies is on */}}
{{- define "cb.localHost" -}}
{{- printf "%s-%s" .root.Release.Name .name -}}
{{- end -}}

{{/*
  Secrets a service needs: key is the name in the Kubernetes Secret and in Key Vault, env is the setting it fills.
  With the keyvault provider the CSI driver writes each one to a file named after the setting.
*/}}
{{- define "cb.secretRefs" -}}
{{- $eventHubs := eq .root.Values.providers.messaging "eventhubs" -}}
{{- if .svc.python }}
- { key: {{ .name }}-database-url, env: DATABASE_URL, file: database_url }
- { key: blob-connection-string, env: BLOB_CONNECTION_STRING, file: blob_connection_string }
{{- if $eventHubs }}
- { key: eventhubs-connection-string, env: EVENTHUBS_CONNECTION_STRING, file: eventhubs_connection_string }
{{- end }}
{{- else }}
{{- if .svc.database }}
{{- $db := printf "ConnectionStrings__%sDb" (.name | replace "-" "" | title | replace "Payersgateway" "Payers") }}
- { key: {{ .name }}-connection-string, env: {{ $db }}, file: {{ $db | replace "__" "--" }} }
{{- end }}
- { key: redis-connection-string, env: ConnectionStrings__Redis, file: ConnectionStrings--Redis }
- { key: blob-connection-string, env: ConnectionStrings__Blob, file: ConnectionStrings--Blob }
- { key: phi-key, env: Phi__Key, file: Phi--Key }
{{- if $eventHubs }}
- { key: eventhubs-connection-string, env: ConnectionStrings__EventHubs, file: ConnectionStrings--EventHubs }
{{- end }}
{{- if eq .name "payers-gateway" }}
- { key: payer-a-callback-token, env: Payers__PAYER-A__CallbackToken, file: Payers--PAYER-A--CallbackToken }
{{- else if eq .name "payer-sim" }}
- { key: payer-a-callback-token, env: Sim__NorthwindCallbackToken, file: Sim--NorthwindCallbackToken }
{{- end }}
{{- end }}
{{- end -}}
