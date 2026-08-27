package com.metagame.backend.domain.room;

import static org.assertj.core.api.Assertions.assertThat;

import java.util.HashSet;
import java.util.Set;
import java.util.stream.IntStream;
import org.junit.jupiter.api.Test;

class RoomManagerTest {

    private final RoomManager roomManager = new RoomManager();

    @Test
    void roomCodeIsShortAndUsesOnlyUnambiguousCharacters() {
        Room room = roomManager.createRoom(1L);

        assertThat(room.getRoomId()).hasSize(RoomManager.CODE_LENGTH);
        assertThat(room.getRoomId().chars())
                .allMatch(c -> RoomManager.CODE_ALPHABET.indexOf(c) >= 0);
    }

    @Test
    void roomCodeAlphabetExcludesConfusableCharacters() {
        assertThat(RoomManager.CODE_ALPHABET).doesNotContain("0", "O", "1", "I", "L");
    }

    @Test
    void everyRoomGetsDistinctCode() {
        Set<String> codes = new HashSet<>();
        IntStream.rangeClosed(1, 500).forEach(i -> codes.add(roomManager.createRoom((long) i).getRoomId()));

        assertThat(codes).hasSize(500);
    }

    @Test
    void codeIsFoundRegardlessOfCaseAndSurroundingSpaces() {
        Room room = roomManager.createRoom(1L);
        String code = room.getRoomId();

        assertThat(roomManager.find(code.toLowerCase())).isSameAs(room);
        assertThat(roomManager.find("  " + code + " ")).isSameAs(room);
    }

    @Test
    void unknownOrBlankCodeReturnsNull() {
        roomManager.createRoom(1L);

        assertThat(roomManager.find("ZZZZZZ")).isNull();
        assertThat(roomManager.find("")).isNull();
        assertThat(roomManager.find(null)).isNull();
    }

    @Test
    void emptyRoomIsRemovedAndLookupIsCaseInsensitive() {
        Room room = roomManager.createRoom(1L);
        room.leave(1L);

        roomManager.removeIfEmpty(room.getRoomId().toLowerCase());

        assertThat(roomManager.find(room.getRoomId())).isNull();
    }

    @Test
    void roomWithPlayersIsNotRemoved() {
        Room room = roomManager.createRoom(1L);

        roomManager.removeIfEmpty(room.getRoomId());

        assertThat(roomManager.find(room.getRoomId())).isSameAs(room);
    }
}
