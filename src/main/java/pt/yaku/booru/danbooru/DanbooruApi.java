package pt.yaku.booru.danbooru;

import java.util.List;

import jakarta.ws.rs.GET;
import jakarta.ws.rs.HeaderParam;
import jakarta.ws.rs.Path;
import jakarta.ws.rs.QueryParam;

@Path("/")
public interface DanbooruApi {

    @GET
    @Path("posts.json")
    List<DanbooruPost> listPosts(@HeaderParam("User-Agent") String userAgent,
                                 @QueryParam("login") String login,
                                 @QueryParam("api_key") String apiKey,
                                 @QueryParam("tags") String tags,
                                 @QueryParam("page") int page,
                                 @QueryParam("limit") int limit);

    @GET
    @Path("tags.json")
    List<DanbooruTag> listTags(@HeaderParam("User-Agent") String userAgent,
                               @QueryParam("login") String login,
                               @QueryParam("api_key") String apiKey,
                               @QueryParam("search[name_matches]") String nameMatches,
                               @QueryParam("search[order]") String order,
                               @QueryParam("limit") int limit);
}
