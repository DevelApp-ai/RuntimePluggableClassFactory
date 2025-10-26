# CRPCF Deployment and Operations Guide

## Overview

This guide provides comprehensive instructions for deploying, configuring, and operating the Containerized RuntimePluggableClassFactory (CRPCF) in production environments.

## 1. Prerequisites

### 1.1 Infrastructure Requirements

#### Kubernetes Environment
- Kubernetes cluster v1.21 or later
- Container runtime: containerd, CRI-O, or Docker
- Storage: Persistent volumes for plugin storage
- Network: CNI-compatible networking (Calico, Flannel, etc.)
- RBAC enabled
- Service mesh (optional but recommended): Istio, Linkerd

#### Azure Container Apps Environment
- Azure subscription with Container Apps enabled
- Resource group for CRPCF resources
- Azure Container Registry (ACR) for container images
- Azure Key Vault for secrets management
- Azure Monitor for logging and metrics

#### Common Requirements
- Certificate authority for code signing certificates
- Container registry (Docker Hub, ACR, ECR, etc.)
- Monitoring infrastructure (Prometheus, Grafana)
- Log aggregation (ELK Stack, Azure Monitor Logs)

### 1.2 Security Prerequisites

#### Code Signing Infrastructure
```bash
# Generate root CA (do this once)
openssl genrsa -out crpcf-root-ca.key 4096
openssl req -new -x509 -days 3650 -key crpcf-root-ca.key -out crpcf-root-ca.crt \
    -subj "/C=US/ST=State/L=City/O=Organization/CN=CRPCF Root CA"

# Generate intermediate CA for code signing
openssl genrsa -out crpcf-codesign-ca.key 4096
openssl req -new -key crpcf-codesign-ca.key -out crpcf-codesign-ca.csr \
    -subj "/C=US/ST=State/L=City/O=Organization/CN=CRPCF CodeSign CA"

openssl x509 -req -days 1825 -in crpcf-codesign-ca.csr \
    -CA crpcf-root-ca.crt -CAkey crpcf-root-ca.key -CAcreateserial \
    -out crpcf-codesign-ca.crt

# Generate code signing certificate for trusted signers
openssl genrsa -out trusted-signer.key 2048
openssl req -new -key trusted-signer.key -out trusted-signer.csr \
    -subj "/C=US/ST=State/L=City/O=TrustedOrg/CN=Trusted Signer"

openssl x509 -req -days 365 -in trusted-signer.csr \
    -CA crpcf-codesign-ca.crt -CAkey crpcf-codesign-ca.key -CAcreateserial \
    -out trusted-signer.crt

# Create PKCS#12 bundle for NuGet signing
openssl pkcs12 -export -out trusted-signer.p12 \
    -inkey trusted-signer.key -in trusted-signer.crt \
    -certfile crpcf-codesign-ca.crt -password pass:YourPassword
```

## 2. Container Images

### 2.1 CRPCF Orchestrator Image

```dockerfile
# File: docker/orchestrator/Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080 9090

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["CRPCF.Orchestrator/CRPCF.Orchestrator.csproj", "CRPCF.Orchestrator/"]
COPY ["CRPCF.Core/CRPCF.Core.csproj", "CRPCF.Core/"]
COPY ["CRPCF.Security/CRPCF.Security.csproj", "CRPCF.Security/"]
COPY ["CRPCF.Orchestrators.Kubernetes/CRPCF.Orchestrators.Kubernetes.csproj", "CRPCF.Orchestrators.Kubernetes/"]
COPY ["CRPCF.Orchestrators.AzureContainerApps/CRPCF.Orchestrators.AzureContainerApps.csproj", "CRPCF.Orchestrators.AzureContainerApps/"]

RUN dotnet restore "CRPCF.Orchestrator/CRPCF.Orchestrator.csproj"
COPY . .
WORKDIR "/src/CRPCF.Orchestrator"
RUN dotnet build "CRPCF.Orchestrator.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CRPCF.Orchestrator.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create non-root user
RUN groupadd -r crpcf && useradd -r -g crpcf -u 1001 crpcf
RUN chown -R crpcf:crpcf /app
USER crpcf

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "CRPCF.Orchestrator.dll"]
```

### 2.2 Plugin Runtime Base Image

```dockerfile
# File: docker/plugin-runtime/Dockerfile
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS base

# Install required packages
RUN apk add --no-cache \
    curl \
    jq \
    bash

# Create plugin user
RUN addgroup -g 1001 plugin && \
    adduser -D -u 1001 -G plugin plugin

# Create plugin directories
RUN mkdir -p /app/plugins /app/runtime /tmp/plugin-workspace && \
    chown -R plugin:plugin /app /tmp/plugin-workspace

# Copy plugin executor
COPY plugin-executor /app/runtime/
RUN chmod +x /app/runtime/plugin-executor && \
    chown plugin:plugin /app/runtime/plugin-executor

USER plugin
WORKDIR /app

# Health check for plugin container
HEALTHCHECK --interval=10s --timeout=5s --start-period=10s --retries=2 \
    CMD /app/runtime/plugin-executor --health-check || exit 1

CMD ["/app/runtime/plugin-executor"]
```

### 2.3 Build and Push Script

```bash
#!/bin/bash
# File: scripts/build-images.sh

set -euo pipefail

REGISTRY=${REGISTRY:-"your-registry.com"}
VERSION=${VERSION:-"2.0.0"}

echo "Building CRPCF images..."

# Build orchestrator image
docker build -t ${REGISTRY}/crpcf/orchestrator:${VERSION} \
    -f docker/orchestrator/Dockerfile .

# Build plugin runtime image
docker build -t ${REGISTRY}/crpcf/plugin-runtime:${VERSION} \
    -f docker/plugin-runtime/Dockerfile .

# Tag latest
docker tag ${REGISTRY}/crpcf/orchestrator:${VERSION} \
    ${REGISTRY}/crpcf/orchestrator:latest
docker tag ${REGISTRY}/crpcf/plugin-runtime:${VERSION} \
    ${REGISTRY}/crpcf/plugin-runtime:latest

echo "Pushing images to registry..."

# Push images
docker push ${REGISTRY}/crpcf/orchestrator:${VERSION}
docker push ${REGISTRY}/crpcf/orchestrator:latest
docker push ${REGISTRY}/crpcf/plugin-runtime:${VERSION}
docker push ${REGISTRY}/crpcf/plugin-runtime:latest

echo "Images built and pushed successfully!"
```

## 3. Kubernetes Deployment

### 3.1 Namespace and RBAC

```yaml
# File: k8s/01-namespace.yaml
apiVersion: v1
kind: Namespace
metadata:
  name: crpcf-system
  labels:
    name: crpcf-system
    istio-injection: enabled
---
apiVersion: v1
kind: Namespace
metadata:
  name: crpcf-plugins
  labels:
    name: crpcf-plugins
    crpcf.io/plugin-namespace: "true"
```

```yaml
# File: k8s/02-rbac.yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: crpcf-orchestrator
  namespace: crpcf-system
---
apiVersion: rbac.authorization.k8s.io/v1
kind: ClusterRole
metadata:
  name: crpcf-orchestrator
rules:
- apiGroups: [""]
  resources: ["pods"]
  verbs: ["create", "delete", "get", "list", "watch", "update", "patch"]
- apiGroups: [""]
  resources: ["pods/exec"]
  verbs: ["create"]
- apiGroups: [""]
  resources: ["pods/log"]
  verbs: ["get", "list"]
- apiGroups: [""]
  resources: ["events"]
  verbs: ["create"]
- apiGroups: ["apps"]
  resources: ["deployments"]
  verbs: ["create", "delete", "get", "list", "watch", "update", "patch"]
---
apiVersion: rbac.authorization.k8s.io/v1
kind: ClusterRoleBinding
metadata:
  name: crpcf-orchestrator
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: ClusterRole
  name: crpcf-orchestrator
subjects:
- kind: ServiceAccount
  name: crpcf-orchestrator
  namespace: crpcf-system
---
apiVersion: v1
kind: ServiceAccount
metadata:
  name: crpcf-plugin
  namespace: crpcf-plugins
---
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata:
  namespace: crpcf-plugins
  name: crpcf-plugin
rules:
- apiGroups: [""]
  resources: ["pods"]
  verbs: ["get"]
- apiGroups: [""]
  resources: ["events"]
  verbs: ["create"]
---
apiVersion: rbac.authorization.k8s.io/v1
kind: RoleBinding
metadata:
  name: crpcf-plugin
  namespace: crpcf-plugins
subjects:
- kind: ServiceAccount
  name: crpcf-plugin
  namespace: crpcf-plugins
roleRef:
  kind: Role
  name: crpcf-plugin
  apiGroup: rbac.authorization.k8s.io
```

### 3.2 Configuration and Secrets

```yaml
# File: k8s/03-config.yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: crpcf-config
  namespace: crpcf-system
data:
  crpcf-config.yaml: |
    crpcf:
      orchestrator:
        name: "CRPCF-K8s"
        version: "2.0.0"
        
      security:
        require_signed_packages: true
        signature_validation_timeout: 30s
        trusted_signers_config_path: "/config/trusted-signers.json"
        validate_certificate_chain: true
        require_valid_certificate_chain: false
        check_certificate_revocation: true
        
      plugin_store:
        type: "filesystem"
        path: "/data/plugins"
        max_size_gb: 100
        cleanup_policy:
          max_age_days: 30
          max_versions_per_plugin: 5
          
      container_orchestration:
        default_platform: "kubernetes"
        base_image_name: "your-registry.com/crpcf/plugin-runtime"
        base_image_tag: "2.0.0"
        platforms:
          kubernetes:
            namespace: "crpcf-plugins"
            service_account: "crpcf-plugin"
            
      monitoring:
        metrics_enabled: true
        logging_level: "info"
        health_check_interval: 30s
        
  trusted-signers.json: |
    {
      "trustedSigners": [
        {
          "signerName": "Your Organization",
          "certificateThumbprint": "A1B2C3D4E5F6789012345678901234567890ABCD",
          "trustLevel": "Standard",
          "allowedPackagePatterns": ["YourOrg.*", "YourOrg.Plugins.*"]
        },
        {
          "signerName": "Microsoft Corporation",
          "certificateThumbprint": "3F3E0316F5E61A2CACCF84E3B31F9F9E7E6D3A71",
          "trustLevel": "SystemLevel",
          "allowedPackagePatterns": ["Microsoft.*", "System.*"]
        }
      ],
      "validationSettings": {
        "requireTimestampSignature": true,
        "allowSelfSignedCertificates": false,
        "certificateRevocationCheck": true,
        "maxCertificateAge": "P2Y"
      }
    }
---
apiVersion: v1
kind: Secret
metadata:
  name: crpcf-certificates
  namespace: crpcf-system
type: Opaque
data:
  # Base64 encoded certificate files
  root-ca.crt: LS0tLS1CRUdJTi... # Your root CA certificate
  codesign-ca.crt: LS0tLS1CRUdJTi... # Your code signing CA certificate
```

### 3.3 Storage

```yaml
# File: k8s/04-storage.yaml
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: crpcf-plugin-store
  namespace: crpcf-system
spec:
  accessModes:
    - ReadWriteOnce
  storageClassName: fast-ssd
  resources:
    requests:
      storage: 100Gi
---
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: crpcf-cache
  namespace: crpcf-system
spec:
  accessModes:
    - ReadWriteOnce
  storageClassName: fast-ssd
  resources:
    requests:
      storage: 20Gi
```

### 3.4 Orchestrator Deployment

```yaml
# File: k8s/05-deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: crpcf-orchestrator
  namespace: crpcf-system
  labels:
    app: crpcf-orchestrator
    version: v2.0.0
spec:
  replicas: 3
  selector:
    matchLabels:
      app: crpcf-orchestrator
  template:
    metadata:
      labels:
        app: crpcf-orchestrator
        version: v2.0.0
      annotations:
        prometheus.io/scrape: "true"
        prometheus.io/port: "9090"
        prometheus.io/path: "/metrics"
    spec:
      serviceAccountName: crpcf-orchestrator
      securityContext:
        runAsNonRoot: true
        runAsUser: 1001
        runAsGroup: 1001
        fsGroup: 1001
      containers:
      - name: orchestrator
        image: your-registry.com/crpcf/orchestrator:2.0.0
        ports:
        - name: http
          containerPort: 8080
          protocol: TCP
        - name: metrics
          containerPort: 9090
          protocol: TCP
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: CRPCF_CONFIG_PATH
          value: "/config/crpcf-config.yaml"
        - name: CRPCF_CERTIFICATES_PATH
          value: "/certs"
        - name: KUBERNETES_NAMESPACE
          valueFrom:
            fieldRef:
              fieldPath: metadata.namespace
        - name: POD_NAME
          valueFrom:
            fieldRef:
              fieldPath: metadata.name
        - name: POD_IP
          valueFrom:
            fieldRef:
              fieldPath: status.podIP
        volumeMounts:
        - name: config
          mountPath: /config
          readOnly: true
        - name: certificates
          mountPath: /certs
          readOnly: true
        - name: plugin-store
          mountPath: /data/plugins
        - name: cache
          mountPath: /cache
        resources:
          requests:
            memory: "512Mi"
            cpu: "250m"
          limits:
            memory: "2Gi"
            cpu: "1000m"
        livenessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 30
          periodSeconds: 30
          timeoutSeconds: 10
          failureThreshold: 3
        readinessProbe:
          httpGet:
            path: /ready
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 10
          timeoutSeconds: 5
          failureThreshold: 3
        securityContext:
          allowPrivilegeEscalation: false
          readOnlyRootFilesystem: true
          capabilities:
            drop:
            - ALL
      volumes:
      - name: config
        configMap:
          name: crpcf-config
      - name: certificates
        secret:
          secretName: crpcf-certificates
      - name: plugin-store
        persistentVolumeClaim:
          claimName: crpcf-plugin-store
      - name: cache
        persistentVolumeClaim:
          claimName: crpcf-cache
      nodeSelector:
        kubernetes.io/arch: amd64
      tolerations:
      - key: "crpcf.io/orchestrator"
        operator: "Equal"
        value: "true"
        effect: "NoSchedule"
      affinity:
        podAntiAffinity:
          preferredDuringSchedulingIgnoredDuringExecution:
          - weight: 100
            podAffinityTerm:
              labelSelector:
                matchExpressions:
                - key: app
                  operator: In
                  values:
                  - crpcf-orchestrator
              topologyKey: kubernetes.io/hostname
```

### 3.5 Services and Ingress

```yaml
# File: k8s/06-services.yaml
apiVersion: v1
kind: Service
metadata:
  name: crpcf-orchestrator
  namespace: crpcf-system
  labels:
    app: crpcf-orchestrator
spec:
  selector:
    app: crpcf-orchestrator
  ports:
  - name: http
    port: 80
    targetPort: 8080
    protocol: TCP
  - name: grpc
    port: 9000
    targetPort: 9000
    protocol: TCP
  type: ClusterIP
---
apiVersion: v1
kind: Service
metadata:
  name: crpcf-orchestrator-metrics
  namespace: crpcf-system
  labels:
    app: crpcf-orchestrator
spec:
  selector:
    app: crpcf-orchestrator
  ports:
  - name: metrics
    port: 9090
    targetPort: 9090
    protocol: TCP
  type: ClusterIP
---
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: crpcf-orchestrator
  namespace: crpcf-system
  annotations:
    kubernetes.io/ingress.class: nginx
    nginx.ingress.kubernetes.io/ssl-redirect: "true"
    nginx.ingress.kubernetes.io/force-ssl-redirect: "true"
    nginx.ingress.kubernetes.io/backend-protocol: "HTTP"
    cert-manager.io/cluster-issuer: "letsencrypt-prod"
spec:
  tls:
  - hosts:
    - crpcf.yourdomain.com
    secretName: crpcf-tls
  rules:
  - host: crpcf.yourdomain.com
    http:
      paths:
      - path: /api
        pathType: Prefix
        backend:
          service:
            name: crpcf-orchestrator
            port:
              number: 80
```

### 3.6 Network Policies

```yaml
# File: k8s/07-network-policies.yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: crpcf-orchestrator-policy
  namespace: crpcf-system
spec:
  podSelector:
    matchLabels:
      app: crpcf-orchestrator
  policyTypes:
  - Ingress
  - Egress
  ingress:
  - from:
    - namespaceSelector:
        matchLabels:
          name: ingress-nginx
    - namespaceSelector:
        matchLabels:
          name: monitoring
    ports:
    - protocol: TCP
      port: 8080
    - protocol: TCP
      port: 9090
  egress:
  - to:
    - namespaceSelector:
        matchLabels:
          name: crpcf-plugins
  - to: []
    ports:
    - protocol: TCP
      port: 443  # HTTPS
    - protocol: TCP
      port: 53   # DNS
    - protocol: UDP
      port: 53   # DNS
---
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: crpcf-plugins-policy
  namespace: crpcf-plugins
spec:
  podSelector:
    matchLabels:
      crpcf.io/plugin: "true"
  policyTypes:
  - Ingress
  - Egress
  ingress:
  - from:
    - namespaceSelector:
        matchLabels:
          name: crpcf-system
  egress:
  - to:
    - namespaceSelector:
        matchLabels:
          name: crpcf-system
    ports:
    - protocol: TCP
      port: 80
  # Block all other egress by default (plugins are isolated)
```

## 4. Monitoring and Observability

### 4.1 Prometheus Configuration

```yaml
# File: monitoring/01-prometheus.yaml
apiVersion: monitoring.coreos.com/v1
kind: ServiceMonitor
metadata:
  name: crpcf-orchestrator
  namespace: crpcf-system
  labels:
    app: crpcf-orchestrator
spec:
  selector:
    matchLabels:
      app: crpcf-orchestrator
  endpoints:
  - port: metrics
    path: /metrics
    interval: 30s
    scrapeTimeout: 10s
---
apiVersion: monitoring.coreos.com/v1
kind: PrometheusRule
metadata:
  name: crpcf-alerts
  namespace: crpcf-system
spec:
  groups:
  - name: crpcf.rules
    rules:
    - alert: CRPCFOrchestratorDown
      expr: up{job="crpcf-orchestrator"} == 0
      for: 1m
      labels:
        severity: critical
      annotations:
        summary: "CRPCF Orchestrator is down"
        description: "CRPCF Orchestrator has been down for more than 1 minute."
    
    - alert: CRPCFHighPluginFailureRate
      expr: rate(crpcf_plugin_executions_failed_total[5m]) > 0.1
      for: 2m
      labels:
        severity: warning
      annotations:
        summary: "High plugin failure rate"
        description: "Plugin execution failure rate is {% raw %}{{ $value }}{% endraw %} failures per second."
    
    - alert: CRPCFHighMemoryUsage
      expr: container_memory_usage_bytes{pod=~"crpcf-orchestrator-.*"} / container_spec_memory_limit_bytes > 0.8
      for: 5m
      labels:
        severity: warning
      annotations:
        summary: "High memory usage"
        description: "CRPCF Orchestrator memory usage is above 80%."
```

### 4.2 Grafana Dashboard

```json
{
  "dashboard": {
    "id": null,
    "title": "CRPCF Orchestrator Dashboard",
    "tags": ["crpcf", "plugins"],
    "timezone": "browser",
    "panels": [
      {
        "id": 1,
        "title": "Plugin Deployments",
        "type": "stat",
        "targets": [
          {
            "expr": "crpcf_plugin_deployments_total",
            "legendFormat": "{% raw %}{{status}}{% endraw %}"
          }
        ],
        "fieldConfig": {
          "defaults": {
            "unit": "short"
          }
        }
      },
      {
        "id": 2,
        "title": "Plugin Execution Rate",
        "type": "graph",
        "targets": [
          {
            "expr": "rate(crpcf_plugin_executions_total[5m])",
            "legendFormat": "Executions/sec"
          }
        ]
      },
      {
        "id": 3,
        "title": "Active Containers",
        "type": "stat",
        "targets": [
          {
            "expr": "crpcf_active_containers",
            "legendFormat": "{% raw %}{{platform}}{% endraw %}"
          }
        ]
      },
      {
        "id": 4,
        "title": "Plugin Execution Duration",
        "type": "heatmap",
        "targets": [
          {
            "expr": "crpcf_plugin_execution_duration_seconds_bucket",
            "legendFormat": "{% raw %}{{le}}{% endraw %}"
          }
        ]
      }
    ],
    "time": {
      "from": "now-1h",
      "to": "now"
    },
    "refresh": "30s"
  }
}
```

### 4.3 Log Aggregation

```yaml
# File: monitoring/02-logging.yaml
apiVersion: logging.coreos.com/v1
kind: ClusterLogForwarder
metadata:
  name: crpcf-logs
  namespace: openshift-logging
spec:
  outputs:
  - name: crpcf-elasticsearch
    type: elasticsearch
    url: https://elasticsearch.yourdomain.com:9200
    secret:
      name: crpcf-elasticsearch-secret
  pipelines:
  - name: crpcf-orchestrator-logs
    inputRefs:
    - application
    filterRefs:
    - crpcf-filter
    outputRefs:
    - crpcf-elasticsearch
---
apiVersion: logging.coreos.com/v1
kind: ClusterLogFilter
metadata:
  name: crpcf-filter
spec:
  type: json
  json:
    javascript: |
      const log = record.log;
      if (log && log.kubernetes && log.kubernetes.namespace_name === 'crpcf-system') {
        return log;
      }
      return null;
```

## 5. Security Hardening

### 5.1 Pod Security Standards

```yaml
# File: security/01-pod-security.yaml
apiVersion: v1
kind: Namespace
metadata:
  name: crpcf-system
  labels:
    pod-security.kubernetes.io/enforce: restricted
    pod-security.kubernetes.io/audit: restricted
    pod-security.kubernetes.io/warn: restricted
---
apiVersion: v1
kind: Namespace
metadata:
  name: crpcf-plugins
  labels:
    pod-security.kubernetes.io/enforce: restricted
    pod-security.kubernetes.io/audit: restricted
    pod-security.kubernetes.io/warn: restricted
```

### 5.2 Security Context Constraints (OpenShift)

```yaml
# File: security/02-scc.yaml
apiVersion: security.openshift.io/v1
kind: SecurityContextConstraints
metadata:
  name: crpcf-orchestrator-scc
allowHostDirVolumePlugin: false
allowHostIPC: false
allowHostNetwork: false
allowHostPID: false
allowHostPorts: false
allowPrivilegeEscalation: false
allowPrivilegedContainer: false
allowedCapabilities: []
defaultAddCapabilities: []
fsGroup:
  type: MustRunAs
  ranges:
  - min: 1001
    max: 1001
readOnlyRootFilesystem: true
requiredDropCapabilities:
- ALL
runAsUser:
  type: MustRunAs
  uid: 1001
seLinuxContext:
  type: MustRunAs
supplementalGroups:
  type: MustRunAs
  ranges:
  - min: 1001
    max: 1001
volumes:
- configMap
- secret
- persistentVolumeClaim
- emptyDir
users:
- system:serviceaccount:crpcf-system:crpcf-orchestrator
```

### 5.3 Admission Controllers

```yaml
# File: security/03-admission-controller.yaml
apiVersion: kyverno.io/v1
kind: Policy
metadata:
  name: crpcf-security-policy
  namespace: crpcf-plugins
spec:
  validationFailureAction: enforce
  background: true
  rules:
  - name: require-non-root-user
    match:
      any:
      - resources:
          kinds:
          - Pod
    validate:
      message: "Pods must run as non-root user"
      pattern:
        spec:
          securityContext:
            runAsNonRoot: true
  
  - name: require-read-only-root-filesystem
    match:
      any:
      - resources:
          kinds:
          - Pod
    validate:
      message: "Containers must have readOnlyRootFilesystem set to true"
      pattern:
        spec:
          containers:
          - securityContext:
              readOnlyRootFilesystem: true
  
  - name: disallow-privilege-escalation
    match:
      any:
      - resources:
          kinds:
          - Pod
    validate:
      message: "Privilege escalation is not allowed"
      pattern:
        spec:
          containers:
          - securityContext:
              allowPrivilegeEscalation: false
```

## 6. Operations and Maintenance

### 6.1 Backup and Disaster Recovery

```bash
#!/bin/bash
# File: scripts/backup-crpcf.sh

set -euo pipefail

BACKUP_DIR=${BACKUP_DIR:-"/backups/crpcf"}
DATE=$(date +%Y%m%d-%H%M%S)
NAMESPACE="crpcf-system"

echo "Starting CRPCF backup at ${DATE}"

# Create backup directory
mkdir -p "${BACKUP_DIR}/${DATE}"

# Backup Kubernetes resources
kubectl get all -n ${NAMESPACE} -o yaml > "${BACKUP_DIR}/${DATE}/k8s-resources.yaml"
kubectl get configmaps -n ${NAMESPACE} -o yaml > "${BACKUP_DIR}/${DATE}/configmaps.yaml"
kubectl get secrets -n ${NAMESPACE} -o yaml > "${BACKUP_DIR}/${DATE}/secrets.yaml"
kubectl get pvc -n ${NAMESPACE} -o yaml > "${BACKUP_DIR}/${DATE}/pvcs.yaml"

# Backup persistent volume data using velero (if available)
if command -v velero &> /dev/null; then
    velero backup create crpcf-backup-${DATE} \
        --include-namespaces ${NAMESPACE},crpcf-plugins \
        --wait
fi

# Backup plugin store (if using external storage)
if [[ "${PLUGIN_STORE_TYPE}" == "s3" ]]; then
    aws s3 sync s3://${PLUGIN_STORE_BUCKET} "${BACKUP_DIR}/${DATE}/plugin-store/"
elif [[ "${PLUGIN_STORE_TYPE}" == "azureblob" ]]; then
    az storage blob download-batch \
        --account-name ${STORAGE_ACCOUNT} \
        --source ${CONTAINER_NAME} \
        --destination "${BACKUP_DIR}/${DATE}/plugin-store/"
fi

echo "Backup completed successfully at ${BACKUP_DIR}/${DATE}"
```

### 6.2 Health Checks and Monitoring

```bash
#!/bin/bash
# File: scripts/health-check.sh

set -euo pipefail

NAMESPACE="crpcf-system"
ORCHESTRATOR_URL="http://crpcf-orchestrator.${NAMESPACE}.svc.cluster.local"

echo "Performing CRPCF health check..."

# Check orchestrator pods
READY_PODS=$(kubectl get pods -n ${NAMESPACE} -l app=crpcf-orchestrator -o jsonpath='{.items[*].status.conditions[?(@.type=="Ready")].status}')
TOTAL_PODS=$(kubectl get pods -n ${NAMESPACE} -l app=crpcf-orchestrator --no-headers | wc -l)
READY_COUNT=$(echo ${READY_PODS} | tr ' ' '\n' | grep -c "True" || true)

if [[ ${READY_COUNT} -lt 2 ]]; then
    echo "ERROR: Only ${READY_COUNT}/${TOTAL_PODS} orchestrator pods are ready"
    exit 1
fi

# Check orchestrator health endpoint
if ! curl -f -s "${ORCHESTRATOR_URL}/health" > /dev/null; then
    echo "ERROR: Orchestrator health endpoint is not responding"
    exit 1
fi

# Check plugin store accessibility
PLUGIN_STORE_HEALTH=$(curl -s "${ORCHESTRATOR_URL}/health/plugin-store" | jq -r '.status')
if [[ "${PLUGIN_STORE_HEALTH}" != "healthy" ]]; then
    echo "ERROR: Plugin store is not healthy: ${PLUGIN_STORE_HEALTH}"
    exit 1
fi

# Check container orchestrator connectivity
CO_HEALTH=$(curl -s "${ORCHESTRATOR_URL}/health/container-orchestrator" | jq -r '.status')
if [[ "${CO_HEALTH}" != "healthy" ]]; then
    echo "ERROR: Container orchestrator is not healthy: ${CO_HEALTH}"
    exit 1
fi

echo "All health checks passed successfully"
```

### 6.3 Scaling Operations

```bash
#!/bin/bash
# File: scripts/scale-orchestrator.sh

set -euo pipefail

NAMESPACE="crpcf-system"
REPLICAS=${1:-3}

echo "Scaling CRPCF orchestrator to ${REPLICAS} replicas..."

kubectl scale deployment crpcf-orchestrator -n ${NAMESPACE} --replicas=${REPLICAS}

# Wait for rollout to complete
kubectl rollout status deployment/crpcf-orchestrator -n ${NAMESPACE} --timeout=300s

echo "Successfully scaled orchestrator to ${REPLICAS} replicas"

# Verify all pods are ready
READY_PODS=$(kubectl get pods -n ${NAMESPACE} -l app=crpcf-orchestrator -o jsonpath='{.items[*].status.conditions[?(@.type=="Ready")].status}' | tr ' ' '\n' | grep -c "True")

if [[ ${READY_PODS} -eq ${REPLICAS} ]]; then
    echo "All ${REPLICAS} pods are ready and healthy"
else
    echo "WARNING: Only ${READY_PODS}/${REPLICAS} pods are ready"
    exit 1
fi
```

### 6.4 Plugin Management Operations

```bash
#!/bin/bash
# File: scripts/manage-plugins.sh

set -euo pipefail

COMMAND=${1:-"list"}
ORCHESTRATOR_URL="http://crpcf-orchestrator.crpcf-system.svc.cluster.local"

case ${COMMAND} in
    "list")
        echo "Listing deployed plugins..."
        curl -s "${ORCHESTRATOR_URL}/api/v1/plugins" | jq '.'
        ;;
    
    "deploy")
        PACKAGE_FILE=${2?"Package file required"}
        PLATFORM=${3:-"kubernetes"}
        
        echo "Deploying plugin from ${PACKAGE_FILE} to ${PLATFORM}..."
        curl -X POST "${ORCHESTRATOR_URL}/api/v1/plugins" \
            -H "Content-Type: multipart/form-data" \
            -F "package=@${PACKAGE_FILE}" \
            -F "targetPlatform=${PLATFORM}" \
            | jq '.'
        ;;
    
    "undeploy")
        PLUGIN_ID=${2?"Plugin ID required"}
        
        echo "Undeploying plugin ${PLUGIN_ID}..."
        curl -X DELETE "${ORCHESTRATOR_URL}/api/v1/plugins/${PLUGIN_ID}" | jq '.'
        ;;
    
    "execute")
        PLUGIN_ID=${2?"Plugin ID required"}
        INPUT_DATA=${3?"Input data required"}
        
        echo "Executing plugin ${PLUGIN_ID} with input: ${INPUT_DATA}"
        curl -X POST "${ORCHESTRATOR_URL}/api/v1/plugins/${PLUGIN_ID}/execute" \
            -H "Content-Type: application/json" \
            -d "{\"inputData\": \"${INPUT_DATA}\"}" \
            | jq '.'
        ;;
    
    "health")
        PLUGIN_ID=${2?"Plugin ID required"}
        
        echo "Checking health of plugin ${PLUGIN_ID}..."
        curl -s "${ORCHESTRATOR_URL}/api/v1/plugins/${PLUGIN_ID}/health" | jq '.'
        ;;
    
    *)
        echo "Usage: $0 {list|deploy|undeploy|execute|health} [arguments...]"
        echo "  list                           - List all deployed plugins"
        echo "  deploy <package> [platform]   - Deploy a plugin package"
        echo "  undeploy <plugin-id>          - Undeploy a plugin"
        echo "  execute <plugin-id> <input>   - Execute a plugin"
        echo "  health <plugin-id>            - Check plugin health"
        exit 1
        ;;
esac
```

## 7. Troubleshooting Guide

### 7.1 Common Issues

#### Issue: Plugin deployment fails with signature validation error
```bash
# Check trusted signers configuration
kubectl get configmap crpcf-config -n crpcf-system -o yaml

# Check certificate validity
openssl x509 -in trusted-signer.crt -text -noout

# Check orchestrator logs
kubectl logs -l app=crpcf-orchestrator -n crpcf-system --tail=100
```

#### Issue: Container creation fails on Kubernetes
```bash
# Check RBAC permissions
kubectl auth can-i create pods --as=system:serviceaccount:crpcf-system:crpcf-orchestrator -n crpcf-plugins

# Check resource quotas
kubectl describe resourcequota -n crpcf-plugins

# Check network policies
kubectl get networkpolicies -n crpcf-plugins
```

#### Issue: Plugin execution timeout
```bash
# Check container resource limits
kubectl top pods -n crpcf-plugins

# Check network connectivity
kubectl exec -it <plugin-pod> -n crpcf-plugins -- ping crpcf-orchestrator.crpcf-system.svc.cluster.local

# Increase timeout in configuration
kubectl patch configmap crpcf-config -n crpcf-system --patch '{"data":{"execution-timeout":"600s"}}'
```

### 7.2 Diagnostic Commands

```bash
# File: scripts/diagnose-crpcf.sh

#!/bin/bash
set -euo pipefail

echo "=== CRPCF Diagnostic Report ==="
echo "Generated at: $(date)"
echo

echo "=== Orchestrator Status ==="
kubectl get pods -n crpcf-system -l app=crpcf-orchestrator -o wide
echo

echo "=== Plugin Pods Status ==="
kubectl get pods -n crpcf-plugins -o wide
echo

echo "=== Recent Events ==="
kubectl get events -n crpcf-system --sort-by='.lastTimestamp' | tail -10
kubectl get events -n crpcf-plugins --sort-by='.lastTimestamp' | tail -10
echo

echo "=== Resource Usage ==="
kubectl top pods -n crpcf-system
kubectl top pods -n crpcf-plugins
echo

echo "=== Persistent Volume Status ==="
kubectl get pvc -n crpcf-system
echo

echo "=== Network Policies ==="
kubectl get networkpolicies -A
echo

echo "=== Recent Orchestrator Logs ==="
kubectl logs -l app=crpcf-orchestrator -n crpcf-system --tail=50 --timestamps
echo

echo "=== Configuration ==="
kubectl get configmap crpcf-config -n crpcf-system -o yaml
```

This comprehensive deployment guide provides everything needed to successfully deploy and operate the Containerized RuntimePluggableClassFactory in production environments.