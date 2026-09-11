# 배포용 이미지 (서버 지급 후 사용)
# 빌드: ./gradlew bootJar && docker build -t metagame-backend .
FROM eclipse-temurin:17-jre
ARG JAR_FILE=build/libs/*-SNAPSHOT.jar
COPY ${JAR_FILE} app.jar
EXPOSE 8080
ENTRYPOINT ["java", "-jar", "/app.jar"]
