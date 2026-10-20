package pt.yaku.user.config;

import java.util.List;

import lombok.AllArgsConstructor;
import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import pt.yaku.booru.Post;

@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
public class FolderData {
    String name;
    List<Post> posts;

}
