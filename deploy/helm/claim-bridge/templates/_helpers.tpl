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
{{- printf "%s/claim-bridge/%s:%s" .root.Values.global.imageRegistry .svc.image .root.Values.global.imageTag -}}
{{- end -}}

{{- define "cb.secretName" -}}
{{- default (printf "%s-secrets" .Release.Name) .Values.global.existingSecret -}}
{{- end -}}

{{/* true when a service is switched on (services are on unless enabled: false) */}}
{{- define "cb.enabled" -}}
{{- if hasKey . "enabled" -}}{{ .enabled }}{{- else -}}true{{- end -}}
{{- end -}}
